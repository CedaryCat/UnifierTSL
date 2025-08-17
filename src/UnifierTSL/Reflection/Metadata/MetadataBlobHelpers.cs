using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using UnifierTSL.Reflection.Metadata.DecodeProviders;

namespace UnifierTSL.Reflection.Metadata
{
    public static class MetadataBlobHelpers
    {
        public static bool IsManagedAssembly(string filePath) {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new PEReader(stream);
            try {
                return reader.HasMetadata;
            }
            catch {
                return false;
            }
        }
        public static PEReader? GetPEReader(Stream stream, bool leaveOpen = false) {
            PEReader? reader = null;
            try {
                if (leaveOpen) {
                    reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
                }
                else {
                    reader = new PEReader(stream);
                }
                if (reader.HasMetadata) {
                    return reader;
                }
            }
            catch {
                reader?.Dispose();
            }
            return null;
        }

        public static bool HasCustomAttribute(MetadataReader metadataReader, string attributeFullName) {
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

        public static bool HasInterface(MetadataReader reader, TypeDefinition typeDef, string interfaceFullName) {
            foreach (var ifaceHandle in typeDef.GetInterfaceImplementations()) {
                var ifaceImpl = reader.GetInterfaceImplementation(ifaceHandle);
                var ifaceType = ifaceImpl.Interface;
                if (ifaceType.Kind == HandleKind.TypeReference) {
                    var typeRef = reader.GetTypeReference((TypeReferenceHandle)ifaceType);
                    var name = reader.GetString(typeRef.Name);
                    var ns = reader.GetString(typeRef.Namespace);
                    if ($"{ns}.{name}" == interfaceFullName) {
                        return true;
                    }
                }
            }
            return false;
        }
        public static bool HasInterface<TInterface>(MetadataReader reader, TypeDefinition typeDef)
            => HasInterface(reader, typeDef, typeof(TInterface).FullName!);

        public static bool HasDefaultConstructor(MetadataReader reader, TypeDefinition typeDef) {
            foreach (var methodHandle in typeDef.GetMethods()) {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var name = reader.GetString(methodDef.Name);
                if (name != ".ctor")
                    continue;

                var sig = methodDef.DecodeSignature(new SimpleTypeProvider(), null);
                if (!methodDef.Attributes.HasFlag(MethodAttributes.Static) &&
                    methodDef.Attributes.HasFlag(MethodAttributes.Public) &&
                    sig.ParameterTypes.Length == 0) {
                    return true;
                }
            }
            return false;
        }

        private static string? GetAttributeFullName(MetadataReader metadataReader, CustomAttribute attribute) {
            string? name;

            var ctorHandle = attribute.Constructor;
            EntityHandle attributeTypeHandle;
            if (ctorHandle.Kind == HandleKind.MemberReference) {
                var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                attributeTypeHandle = memberRef.Parent;

                if (attributeTypeHandle.Kind == HandleKind.TypeSpecification) {
                    var typeSpec = metadataReader.GetTypeSpecification((TypeSpecificationHandle)attributeTypeHandle);

                    var decoder = new Type2SimpleTextProvider();
                    var sigDecoder = new SignatureDecoder<string, object?>(decoder, metadataReader, null!);

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


        static string GetFullTypeName(MetadataReader reader, TypeReferenceHandle handle) {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);
            var namespaceName = reader.GetString(typeRef.Namespace);
            return $"{namespaceName}.{name}";
        }
        public static List<CustomAttribute> ExtractCustomAttributeOnSpecificTypes(MetadataReader metadataReader, string targetNamespace, string attributeName) {
            List<CustomAttribute> customAttributes = [];

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

        public static bool TryReadAssemblyIdentity(MetadataReader metadataReader, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out Version? version) {
            name = null;
            version = null;

            var assemblyDef = metadataReader.GetAssemblyDefinition();
            name = metadataReader.GetString(assemblyDef.Name);
            version = assemblyDef.Version;

            if (metadataReader.TryReadVersionAttribute(assemblyDef, nameof(AssemblyFileVersionAttribute), out version)) {
                return true;
            }
            if (metadataReader.TryReadVersionAttribute(assemblyDef, nameof(AssemblyVersionAttribute), out version)) {
                return true;
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

        public static string ReadAssemblyName(MetadataReader metadataReader) {
            var assemblyDef = metadataReader.GetAssemblyDefinition();
            return metadataReader.GetString(assemblyDef.Name);
        }

        public static bool TryReadAssemblyAttributeData(MetadataReader metadataReader, string attributeFullName, out ParsedCustomAttribute attributeData) {
            attributeData = default;
            var assemblyDefinition = metadataReader.GetAssemblyDefinition();

            foreach (var handle in assemblyDefinition.GetCustomAttributes()) {
                var attribute = metadataReader.GetCustomAttribute(handle);
                var fullName = GetAttributeFullName(metadataReader, attribute);

                if (fullName != attributeFullName) {
                    continue;
                }

                return AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out attributeData);
            }
            return false;
        }
        public static bool TryReadTypeAttributeData(MetadataReader metadataReader, TypeDefinition typeDef, string attributeFullName, out ParsedCustomAttribute attributeData) {
            attributeData = default;
            foreach (var handle in typeDef.GetCustomAttributes()) {
                var attribute = metadataReader.GetCustomAttribute(handle);
                var fullName = GetAttributeFullName(metadataReader, attribute);

                if (fullName != attributeFullName) {
                    continue;
                }

                return AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out attributeData);
            }
            return false;
        }
    }
}