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
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using PEReader reader = new(stream);
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
            AssemblyDefinition assemblyDefinition = metadataReader.GetAssemblyDefinition();

            foreach (CustomAttributeHandle handle in assemblyDefinition.GetCustomAttributes()) {
                CustomAttribute attribute = metadataReader.GetCustomAttribute(handle);
                string? fullName = GetAttributeFullName(metadataReader, attribute);

                if (fullName == attributeFullName) {
                    return true;
                }
            }

            return false;
        }

        public static bool HasInterface(MetadataReader reader, TypeDefinition typeDef, string interfaceFullName) {
            foreach (InterfaceImplementationHandle ifaceHandle in typeDef.GetInterfaceImplementations()) {
                InterfaceImplementation ifaceImpl = reader.GetInterfaceImplementation(ifaceHandle);
                EntityHandle ifaceType = ifaceImpl.Interface;
                if (ifaceType.Kind == HandleKind.TypeReference) {
                    TypeReference typeRef = reader.GetTypeReference((TypeReferenceHandle)ifaceType);
                    string name = reader.GetString(typeRef.Name);
                    string ns = reader.GetString(typeRef.Namespace);
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
            foreach (MethodDefinitionHandle methodHandle in typeDef.GetMethods()) {
                MethodDefinition methodDef = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(methodDef.Name);
                if (name != ".ctor")
                    continue;

                MethodSignature<Type> sig = methodDef.DecodeSignature(new SimpleTypeProvider(), null);
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

            EntityHandle ctorHandle = attribute.Constructor;
            EntityHandle attributeTypeHandle;
            if (ctorHandle.Kind == HandleKind.MemberReference) {
                MemberReference memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                attributeTypeHandle = memberRef.Parent;
                name = GetNameFomTypeHandle(metadataReader, attributeTypeHandle);
            }
            else if (ctorHandle.Kind == HandleKind.MethodDefinition) {
                MethodDefinition methodDef = metadataReader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
                attributeTypeHandle = methodDef.GetDeclaringType();

                name = GetNameFomTypeHandle(metadataReader, attributeTypeHandle);
            }
            else {
                name = null;
            }

            return name;

            static string? GetNameFomTypeHandle(MetadataReader metadataReader, EntityHandle attributeTypeHandle) {
                string? name;
                if (attributeTypeHandle.Kind == HandleKind.TypeSpecification) {
                    TypeSpecification typeSpec = metadataReader.GetTypeSpecification((TypeSpecificationHandle)attributeTypeHandle);

                    Type2SimpleTextProvider decoder = new();
                    SignatureDecoder<string, object?> sigDecoder = new(decoder, metadataReader, null!);

                    BlobReader blobReader = metadataReader.GetBlobReader(typeSpec.Signature);
                    name = sigDecoder.DecodeType(ref blobReader);
                }
                else if (attributeTypeHandle.Kind == HandleKind.TypeDefinition) {
                    name = GetFullTypeName(metadataReader, (TypeDefinitionHandle)attributeTypeHandle);
                }
                else if (attributeTypeHandle.Kind == HandleKind.TypeReference) {
                    name = GetFullTypeName(metadataReader, (TypeReferenceHandle)attributeTypeHandle);
                }
                else {
                    name = null;
                }

                return name;
            }
        }


        private static string GetFullTypeName(MetadataReader reader, TypeReferenceHandle handle) {
            TypeReference typeRef = reader.GetTypeReference(handle);
            string name = reader.GetString(typeRef.Name);
            string namespaceName = reader.GetString(typeRef.Namespace);
            return $"{namespaceName}.{name}";
        }
        private static string GetFullTypeName(MetadataReader reader, TypeDefinitionHandle handle) {
            TypeDefinition typeRef = reader.GetTypeDefinition(handle);
            string name = reader.GetString(typeRef.Name);
            string namespaceName = reader.GetString(typeRef.Namespace);
            return $"{namespaceName}.{name}";
        }
        public static List<CustomAttribute> ExtractCustomAttributeOnSpecificTypes(MetadataReader metadataReader, string targetNamespace, string attributeName) {
            List<CustomAttribute> customAttributes = [];

            foreach (TypeDefinitionHandle typeDefHandle in metadataReader.TypeDefinitions) {
                TypeDefinition typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                string ns = metadataReader.GetString(typeDef.Namespace);
                string name = metadataReader.GetString(typeDef.Name);

                if (!ns.Equals(targetNamespace, StringComparison.Ordinal))
                    continue;

                foreach (CustomAttributeHandle attrHandle in typeDef.GetCustomAttributes()) {
                    CustomAttribute attr = metadataReader.GetCustomAttribute(attrHandle);
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

            AssemblyDefinition assemblyDef = metadataReader.GetAssemblyDefinition();
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

        private static bool IsAttribute(MetadataReader metadataReader, CustomAttribute attribute, string attributeName) {
            EntityHandle ctor = attribute.Constructor;

            string? attrTypeName = null;

            if (ctor.Kind == HandleKind.MemberReference) {
                MemberReference memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctor);
                if (memberRef.Parent.Kind == HandleKind.TypeReference) {
                    TypeReference typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                    attrTypeName = metadataReader.GetString(typeRef.Name);
                }
            }
            else if (ctor.Kind == HandleKind.MethodDefinition) {
                MethodDefinition methodDef = metadataReader.GetMethodDefinition((MethodDefinitionHandle)ctor);
                TypeDefinition typeDef = metadataReader.GetTypeDefinition(methodDef.GetDeclaringType());
                attrTypeName = metadataReader.GetString(typeDef.Name);
            }

            return attrTypeName == attributeName;
        }
        private static bool TryReadVersionAttribute(this MetadataReader metadataReader, AssemblyDefinition assemblyDef, string attributeName, [NotNullWhen(true)] out Version? version) {
            foreach (CustomAttributeHandle handle in assemblyDef.GetCustomAttributes()) {
                CustomAttribute attribute = metadataReader.GetCustomAttribute(handle);
                if (IsAttribute(metadataReader, attribute, attributeName)) {
                    AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out ParsedCustomAttribute parsed);
                    if (parsed.ConstructorArguments.First() is string fileVersion && Version.TryParse(fileVersion, out Version? parsedVersion)) {
                        version = parsedVersion;
                        return true;
                    }
                }
            }
            version = null;
            return false;
        }

        public static string ReadAssemblyName(MetadataReader metadataReader) {
            AssemblyDefinition assemblyDef = metadataReader.GetAssemblyDefinition();
            return metadataReader.GetString(assemblyDef.Name);
        }

        public static bool TryReadAssemblyAttributeData(MetadataReader metadataReader, string attributeFullName, out ParsedCustomAttribute attributeData) {
            attributeData = default;
            AssemblyDefinition assemblyDefinition = metadataReader.GetAssemblyDefinition();

            foreach (CustomAttributeHandle handle in assemblyDefinition.GetCustomAttributes()) {
                CustomAttribute attribute = metadataReader.GetCustomAttribute(handle);
                string? fullName = GetAttributeFullName(metadataReader, attribute);

                if (fullName != attributeFullName) {
                    continue;
                }

                return AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out attributeData);
            }
            return false;
        }
        public static bool TryReadTypeAttributeData(MetadataReader metadataReader, TypeDefinition typeDef, string attributeFullName, out ParsedCustomAttribute attributeData) {
            attributeData = default;
            foreach (CustomAttributeHandle handle in typeDef.GetCustomAttributes()) {
                CustomAttribute attribute = metadataReader.GetCustomAttribute(handle);
                string? fullName = GetAttributeFullName(metadataReader, attribute);

                if (fullName != attributeFullName) {
                    continue;
                }

                return AttributeParser.TryParseCustomAttribute(attribute, metadataReader, out attributeData);
            }
            return false;
        }
    }
}