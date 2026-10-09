using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace AbiCheck;

/// <summary>
/// Resolves every type and member referenced by an assembly against the target reference assemblies
/// (TaleWorlds assemblies by default). Verifies that referenced symbols exist with the exact signatures
/// expected at compile time to detect runtime binding failures such as <see cref="TypeLoadException"/>,
/// <see cref="MissingMethodException"/>, or <see cref="MissingFieldException"/>.
/// </summary>
internal static class ReferenceChecker
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static (int Checked, IReadOnlyCollection<string> Missing) Check(string assemblyPath, IReadOnlyList<string> referenceDirectories, IReadOnlyList<string>? assemblyPrefixes = null)
    {
        assemblyPrefixes ??= ["TaleWorlds"];
        var paths = referenceDirectories.SelectMany(directory => Directory.GetFiles(directory, "*.dll")).ToList();
        paths.AddRange(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
        // The first directory takes precedence: prioritize netstandard2.0 assemblies over net472, matching project build references.
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths.GroupBy(Path.GetFileName).Select(group => group.First())));

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var signatures = new SignatureFormatter();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var checkedCount = 0;

        string AssemblyOf(TypeReferenceHandle handle)
        {
            var reference = reader.GetTypeReference(handle);
            while (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                reference = reader.GetTypeReference((TypeReferenceHandle) reference.ResolutionScope);
            return reference.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle) reference.ResolutionScope).Name)
                : "";
        }

        bool IsChecked(TypeReferenceHandle handle) => assemblyPrefixes.Any(prefix => AssemblyOf(handle).StartsWith(prefix, StringComparison.Ordinal));

        Type? Resolve(TypeReferenceHandle handle, out string name)
        {
            var reference = reader.GetTypeReference(handle);
            var typeName = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var declaring = Resolve((TypeReferenceHandle) reference.ResolutionScope, out var declaringName);
                name = declaringName + "+" + typeName;
                return declaring?.GetNestedType(typeName, BindingFlags.Public | BindingFlags.NonPublic);
            }
            var ns = reader.GetString(reference.Namespace);
            var fullName = ns.Length == 0 ? typeName : ns + "." + typeName;
            var assemblyName = AssemblyOf(handle);
            name = $"[{assemblyName}]{fullName}";
            try
            {
                return context.LoadFromAssemblyName(assemblyName).GetType(fullName, throwOnError: false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        foreach (var handle in reader.TypeReferences)
        {
            if (!IsChecked(handle))
                continue;
            checkedCount++;
            if (Resolve(handle, out var name) is null)
                missing.Add($"type {name}");
        }

        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (DeclaringType(reader, member.Parent) is not { } declaringHandle || !IsChecked(declaringHandle))
                continue;

            checkedCount++;
            var declaring = Resolve(declaringHandle, out var declaringName);
            var memberName = reader.GetString(member.Name);
            if (declaring is null)
            {
                missing.Add($"member {declaringName}::{memberName} (its type is missing)");
                continue;
            }

            if (member.GetKind() == MemberReferenceKind.Field)
            {
                var fieldType = member.DecodeFieldSignature(signatures, null);
                var field = Hierarchy(declaring).Select(type => type.GetField(memberName, All)).FirstOrDefault(found => found is not null);
                if (field is null || SignatureFormatter.Format(field.FieldType) != fieldType)
                    missing.Add($"field {declaringName}::{memberName} : {fieldType}");
                continue;
            }

            var signature = member.DecodeMethodSignature(signatures, null);
            var candidates = memberName == ".ctor"
                ? declaring.GetConstructors(All).Cast<MethodBase>()
                : Hierarchy(declaring).SelectMany(type => type.GetMethods(All)).Where(method => method.Name == memberName);
            if (!candidates.Any(method => Matches(method, signature)))
                missing.Add($"method {declaringName}::{memberName}({string.Join(", ", signature.ParameterTypes)}) : {signature.ReturnType}");
        }

        return (checkedCount, missing);
    }

    /// <summary>
    /// Gets the type on which a member reference is declared. For generic instances, returns the generic type definition.
    /// </summary>
    private static TypeReferenceHandle? DeclaringType(MetadataReader reader, EntityHandle parent)
    {
        if (parent.Kind == HandleKind.TypeReference)
            return (TypeReferenceHandle) parent;
        if (parent.Kind != HandleKind.TypeSpecification)
            return null;

        var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle) parent).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
            return null;
        blob.ReadByte(); // CLASS or VALUETYPE
        var definition = blob.ReadTypeHandle();
        return definition.Kind == HandleKind.TypeReference ? (TypeReferenceHandle) definition : null;
    }

    private static bool Matches(MethodBase method, MethodSignature<string> signature)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != signature.ParameterTypes.Length || method.IsGenericMethodDefinition != signature.GenericParameterCount > 0)
            return false;
        if (method.IsGenericMethodDefinition && method.GetGenericArguments().Length != signature.GenericParameterCount)
            return false;
        if (method.IsStatic != !signature.Header.IsInstance)
            return false;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (SignatureFormatter.Format(parameters[i].ParameterType) != signature.ParameterTypes[i])
                return false;
        }
        var returnType = method is MethodInfo info ? SignatureFormatter.Format(info.ReturnType) : "System.Void";
        return returnType == signature.ReturnType;
    }

    private static IEnumerable<Type> Hierarchy(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            yield return current;
    }
}

/// <summary>
/// Formats type signatures consistently across ECMA-335 metadata tokens and System.Type reflection objects
/// so they can be compared directly. Distinguishes value types with a prefix because structs and classes
/// with identical names are treated as distinct types at runtime.
/// </summary>
internal sealed class SignatureFormatter : ISignatureTypeProvider<string, object?>
{
    private const string ValueType = "valuetype ";

    private static readonly HashSet<string> Primitives =
    [
        "System.Boolean", "System.Char", "System.SByte", "System.Byte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.IntPtr", "System.UIntPtr", "System.String",
        "System.Object", "System.Void", "System.TypedReference",
    ];

    public static string Format(Type type)
    {
        if (type.IsGenericParameter)
            return (type.DeclaringMethod is not null ? "!!" : "!") + type.GenericParameterPosition;
        if (type.IsByRef)
            return Format(type.GetElementType()!) + "&";
        if (type.IsPointer)
            return Format(type.GetElementType()!) + "*";
        if (type.IsArray)
            return Format(type.GetElementType()!) + (type.IsSZArray ? "[]" : "[" + new string(',', type.GetArrayRank() - 1) + "]");
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            return Format(type.GetGenericTypeDefinition()) + "<" + string.Join(",", type.GetGenericArguments().Select(Format)) + ">";

        var name = Name(type);
        return type.IsValueType && !Primitives.Contains(name) ? ValueType + name : name;
    }

    private static string Name(Type type) => type.IsNested
        ? Name(type.DeclaringType!) + "+" + type.Name
        : string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode switch
    {
        PrimitiveTypeCode.Object => "Object",
        PrimitiveTypeCode.Void => "Void",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var name = ReferenceName(reader, handle);
        return rawTypeKind == (byte) SignatureTypeKind.ValueType && !Primitives.Contains(name) ? ValueType + name : name;
    }

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Namespace) is { Length: > 0 } ns ? ns + "." + reader.GetString(definition.Name) : reader.GetString(definition.Name);
        return (rawTypeKind == (byte) SignatureTypeKind.ValueType ? ValueType : "") + name;
    }

    private static string ReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return ReferenceName(reader, (TypeReferenceHandle) reference.ResolutionScope) + "+" + name;
        var ns = reader.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method " + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";
}
