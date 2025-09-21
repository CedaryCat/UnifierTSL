using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace UnifierTSL.Reflection.Metadata.DecodeProviders
{
    public class Type2SimpleTextProvider() : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) {
            TypeDefinition typeDef = reader.GetTypeDefinition(handle);
            string name = reader.GetString(typeDef.Name);
            string ns = reader.GetString(typeDef.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) {
            TypeReference typeRef = reader.GetTypeReference(handle);
            string name = reader.GetString(typeRef.Name);
            string ns = reader.GetString(typeRef.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
            TypeSpecification typeSpec = reader.GetTypeSpecification(handle);
            BlobReader blobReader = reader.GetBlobReader(typeSpec.Signature);
            SignatureDecoder<string, object?> decoder = new(this, reader, genericContext);
            return decoder.DecodeType(ref blobReader);
        }
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetByReferenceType(string elementType) => "ref " + elementType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    }
}
