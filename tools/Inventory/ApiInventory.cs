using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Aperture.Inventory;

public sealed record ApiMember(string Kind, string Name, string Signature, string? Constant);
public sealed record ApiType(string Name, string Kind, string[] Interfaces, ApiMember[] Members);
public sealed record AssemblyInventory(string Assembly, string Version, string Sha256, ApiType[] Types);

public static class ApiInventory
{
    public static AssemblyInventory Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(bytes));
        var reader = pe.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        var signatures = new SignatureNames(reader);
        var types = new List<ApiType>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!Visible(handle)) continue;
            var members = new List<ApiMember>();
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
                var name = reader.GetString(method.Name);
                var sig = method.DecodeSignature(signatures, (object?)null);
                var generic = sig.GenericParameterCount > 0 ? "<" + string.Join(",", Enumerable.Range(0, sig.GenericParameterCount).Select(n => "!!" + n)) + ">" : "";
                members.Add(new("method", name, sig.ReturnType + " " + name + generic + "(" + string.Join(", ", sig.ParameterTypes) + ")", null));
            }
            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (!Public(accessors.Getter) && !Public(accessors.Setter)) continue;
                var sig = property.DecodeSignature(signatures, (object?)null);
                var name = reader.GetString(property.Name);
                members.Add(new("property", name, sig.ReturnType + " " + name + (sig.ParameterTypes.Length == 0 ? "" : "[" + string.Join(", ", sig.ParameterTypes) + "]") + " { " + (Public(accessors.Getter) ? "get; " : "") + (Public(accessors.Setter) ? "set; " : "") + "}", null));
            }
            foreach (var eventHandle in type.GetEvents())
            {
                var evt = reader.GetEventDefinition(eventHandle);
                var accessors = evt.GetAccessors();
                if (!Public(accessors.Adder) && !Public(accessors.Remover)) continue;
                var name = reader.GetString(evt.Name);
                members.Add(new("event", name, signatures.Name(evt.Type) + " " + name, null));
            }
            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public) continue;
                var name = reader.GetString(field.Name);
                members.Add(new("field", name, field.DecodeSignature(signatures, (object?)null) + " " + name, ConstantValue(field.GetDefaultValue())));
            }
            var baseName = type.BaseType.IsNil ? "" : signatures.Name(type.BaseType);
            var kind = type.Attributes.HasFlag(TypeAttributes.Interface) ? "interface" : baseName switch { "System.Enum" => "enum", "System.ValueType" => "struct", "System.MulticastDelegate" => "delegate", _ => "class" };
            var interfaces = type.GetInterfaceImplementations().Select(i => signatures.Name(reader.GetInterfaceImplementation(i).Interface)).Order(StringComparer.Ordinal).ToArray();
            types.Add(new(signatures.GetTypeFromDefinition(reader, handle, 0), kind, interfaces, members.OrderBy(m => m.Kind, StringComparer.Ordinal).ThenBy(m => m.Signature, StringComparer.Ordinal).ToArray()));
        }
        return new(reader.GetString(assembly.Name), assembly.Version.ToString(), Convert.ToHexString(SHA256.HashData(bytes)), types.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray());

        bool Public(MethodDefinitionHandle h) => !h.IsNil && (reader.GetMethodDefinition(h).Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        bool Visible(TypeDefinitionHandle h)
        {
            var t = reader.GetTypeDefinition(h);
            var visibility = t.Attributes & TypeAttributes.VisibilityMask;
            return visibility == TypeAttributes.Public || visibility == TypeAttributes.NestedPublic && Visible(t.GetDeclaringType());
        }
        string? ConstantValue(ConstantHandle h)
        {
            if (h.IsNil) return null;
            var c = reader.GetConstant(h);
            var b = reader.GetBlobReader(c.Value);
            object? value = c.TypeCode switch
            {
                ConstantTypeCode.Boolean => b.ReadBoolean(), ConstantTypeCode.Byte => b.ReadByte(), ConstantTypeCode.SByte => b.ReadSByte(),
                ConstantTypeCode.Int16 => b.ReadInt16(), ConstantTypeCode.UInt16 => b.ReadUInt16(), ConstantTypeCode.Int32 => b.ReadInt32(),
                ConstantTypeCode.UInt32 => b.ReadUInt32(), ConstantTypeCode.Int64 => b.ReadInt64(), ConstantTypeCode.UInt64 => b.ReadUInt64(),
                ConstantTypeCode.Single => b.ReadSingle(), ConstantTypeCode.Double => b.ReadDouble(), ConstantTypeCode.Char => (char)b.ReadUInt16(),
                ConstantTypeCode.String => b.ReadUTF16(b.Length), ConstantTypeCode.NullReference => null, _ => throw new BadImageFormatException("Unknown constant type")
            };
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }
    }
}

internal sealed class SignatureNames(MetadataReader metadata) : ISignatureTypeProvider<string, object?>
{
    public string Name(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(metadata, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => GetTypeFromReference(metadata, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(metadata, null, (TypeSpecificationHandle)handle, 0),
        _ => throw new BadImageFormatException("Invalid type handle")
    };
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var t = reader.GetTypeDefinition(handle);
        var parent = t.GetDeclaringType();
        return parent.IsNil ? Join(reader.GetString(t.Namespace), reader.GetString(t.Name)) : GetTypeFromDefinition(reader, parent, 0) + "+" + reader.GetString(t.Name);
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var t = reader.GetTypeReference(handle);
        return t.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(reader, (TypeReferenceHandle)t.ResolutionScope, 0) + "+" + reader.GetString(t.Name) : Join(reader.GetString(t.Namespace), reader.GetString(t.Name));
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode code) => "System." + code.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    private static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
