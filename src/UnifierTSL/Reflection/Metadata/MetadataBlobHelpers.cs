using NuGet.Protocol.Plugins;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace UnifierTSL.Reflection.Metadata
{
    public static class MetadataBlobHelpers
    {
        public static bool IsManagedAssembly(string filePath) {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        public static bool HasCustomAttribute(Stream assemblyStream, string attributeFullName) {
            try {
                using var peReader = new PEReader(assemblyStream);

                if (!peReader.HasMetadata) {
                    return false;
                }

                var metadataReader = peReader.GetMetadataReader();
                var assemblyDefinition = metadataReader.GetAssemblyDefinition();

                foreach (var handle in assemblyDefinition.GetCustomAttributes()) {
                    var attribute = metadataReader.GetCustomAttribute(handle);
                    var fullName = GetAttributeFullName(metadataReader, attribute);

                    if (fullName == attributeFullName) {
                        return true;
                    }
                }

                return false;
            }
            catch {
                return false;
            }
        }

        private static string? GetAttributeFullName(MetadataReader metadataReader, CustomAttribute attribute) {
            string? name;

            var ctorHandle = attribute.Constructor;
            EntityHandle attributeTypeHandle;
            if (ctorHandle.Kind == HandleKind.MemberReference) {
                var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                attributeTypeHandle = memberRef.Parent;

                if (attributeTypeHandle.Kind == HandleKind.TypeSpecification) {
                    var typeSpecHandle = (TypeSpecificationHandle)attributeTypeHandle;
                    var typeSpec = metadataReader.GetTypeSpecification((TypeSpecificationHandle)attributeTypeHandle);

                    var decoder = new Type2TextProvider(metadataReader);
                    var sigDecoder = new SignatureDecoder<string, object>(decoder, metadataReader, null);

                    BlobReader blobReader = metadataReader.GetBlobReader(typeSpec.Signature);
                    name = sigDecoder.DecodeType(ref blobReader);
                }
                else if (attributeTypeHandle.Kind == HandleKind.TypeReference) {
                    name = GetFullTypeName(metadataReader, (TypeReferenceHandle)attributeTypeHandle);
                }
                else {
                    name = null;
                }
            }
            else if (ctorHandle.Kind == HandleKind.MethodDefinition) {
                var methodDef = metadataReader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
                attributeTypeHandle = methodDef.GetDeclaringType();
                name = GetFullTypeName(metadataReader, (TypeReferenceHandle)attributeTypeHandle);
            }
            else {
                name = null;
            }

            return name;
        }

        class Type2TextProvider : ISignatureTypeProvider<string, object>
        {
            private readonly MetadataReader _reader;

            public Type2TextProvider(MetadataReader reader) {
                _reader = reader;
            }

            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) {
                var typeDef = reader.GetTypeDefinition(handle);
                var name = reader.GetString(typeDef.Name);
                var ns = reader.GetString(typeDef.Namespace);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) {
                var typeRef = reader.GetTypeReference(handle);
                var name = reader.GetString(typeRef.Name);
                var ns = reader.GetString(typeRef.Namespace);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
                var typeSpec = reader.GetTypeSpecification(handle);
                BlobReader blobReader = reader.GetBlobReader(typeSpec.Signature);
                var decoder = new SignatureDecoder<string, object>(this, reader, genericContext);
                return decoder.DecodeType(ref blobReader);
            }
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}";
            public string GetGenericMethodParameter(object genericContext, int index) => $"!!{index}";
            public string GetGenericTypeParameter(object genericContext, int index) => $"!{index}";
            public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        }

        static string GetFullTypeName(MetadataReader reader, TypeReferenceHandle handle) {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);
            var namespaceName = reader.GetString(typeRef.Namespace);
            return $"{namespaceName}.{name}";
        }
        public static List<CustomAttribute> ExtractCustomAttributeOnSpecificTypes(PEReader peReader, string targetNamespace, string attributeName) {
            List<CustomAttribute> customAttributes = [];

            if (!peReader.HasMetadata) {
                return customAttributes;
            }

            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions) {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                string ns = metadataReader.GetString(typeDef.Namespace);
                string name = metadataReader.GetString(typeDef.Name);

                if (!ns.Equals(targetNamespace, StringComparison.Ordinal))
                    continue;

                foreach (var attrHandle in typeDef.GetCustomAttributes()) {
                    var attr = metadataReader.GetCustomAttribute(attrHandle);
                    if (IsAttribute(metadataReader, attr, attributeName)) {
                        customAttributes.Add(attr);
                    }
                }
            }

            return customAttributes;
        }

        public static bool TryReadAssemblyIdentity(Stream stream, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out Version? version) {
            name = null;
            version = null;
            try {
                using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);

                if (!peReader.HasMetadata) {
                    return false;
                }
                var metadataReader = peReader.GetMetadataReader();

                var assemblyDef = metadataReader.GetAssemblyDefinition();
                name = metadataReader.GetString(assemblyDef.Name);
                version = assemblyDef.Version;

                if (metadataReader.TryReadVersionAttribute(assemblyDef, nameof(AssemblyFileVersionAttribute), out version)) {
                    return true;
                }
                if (metadataReader.TryReadVersionAttribute(assemblyDef, nameof(AssemblyVersionAttribute), out version)) {
                    return true;
                }
            }
            catch {
            }
            return false;
        }

        static bool IsAttribute(MetadataReader metadataReader, CustomAttribute attribute, string attributeName) {
            var ctor = attribute.Constructor;

            string? attrTypeName = null;

            if (ctor.Kind == HandleKind.MemberReference) {
                var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctor);
                if (memberRef.Parent.Kind == HandleKind.TypeReference) {
                    var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                    attrTypeName = metadataReader.GetString(typeRef.Name);
                }
            }
            else if (ctor.Kind == HandleKind.MethodDefinition) {
                var methodDef = metadataReader.GetMethodDefinition((MethodDefinitionHandle)ctor);
                var typeDef = metadataReader.GetTypeDefinition(methodDef.GetDeclaringType());
                attrTypeName = metadataReader.GetString(typeDef.Name);
            }

            return attrTypeName == attributeName;
        }

        private static bool TryReadVersionAttribute(this MetadataReader metadataReader, AssemblyDefinition assemblyDef, string attributeName, [NotNullWhen(true)] out Version? version) {
            foreach (var handle in assemblyDef.GetCustomAttributes()) {
                var attribute = metadataReader.GetCustomAttribute(handle);
                if (IsAttribute(metadataReader, attribute, attributeName)) {
                    AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out var parsed);
                    if (parsed.ConstructorArguments.First() is string fileVersion && Version.TryParse(fileVersion, out var parsedVersion)) {
                        version = parsedVersion;
                        return true;
                    }
                }
            }
            version = null;
            return false;
        }

        public static int ReadCompressedUInt32(byte[] buffer, ref int index) {
            byte b = buffer[index++];
            if ((b & 0x80) == 0) {
                return b;
            }
            else if ((b & 0xC0) == 0x80) {
                return (b & 0x3F) << 8 | buffer[index++];
            }
            else {
                return (b & 0x1F) << 24 | buffer[index++] << 16 | buffer[index++] << 8 | buffer[index++];
            }
        }

        public static string? TryReadStringFromAttributeBlob(byte[] blob) {
            // See ECMA-335 II.23.3: Blob encoding: 0x01 + string length + UTF8 bytes
            try {
                int index = 0;
                if (blob[index++] != 0x01) return null; // prolog

                int length = ReadCompressedUInt32(blob, ref index);
                string result = Encoding.UTF8.GetString(blob, index, length);
                return result;
            }
            catch {
                return null;
            }
        }
    }
}