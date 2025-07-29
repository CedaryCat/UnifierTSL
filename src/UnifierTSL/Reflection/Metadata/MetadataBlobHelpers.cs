using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
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

                foreach (var handle in metadataReader.CustomAttributes) {
                    var attribute = metadataReader.GetCustomAttribute(handle);
                    var ctorHandle = attribute.Constructor;

                    if (ctorHandle.Kind == HandleKind.MemberReference) {
                        var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                        var container = memberRef.Parent;

                        if (container.Kind == HandleKind.TypeReference) {
                            var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)container);
                            var fullName = metadataReader.GetString(typeRef.Namespace) + "." + metadataReader.GetString(typeRef.Name);

                            if (fullName == attributeFullName) {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch {
                return false;
            }
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

        private static bool TryReadVersionAttribute(this MetadataReader metadataReader, AssemblyDefinition assemblyDef, string attributeName, [NotNullWhen(true)] out Version? version) {
            foreach (var handle in assemblyDef.GetCustomAttributes()) {
                var attribute = metadataReader.GetCustomAttribute(handle);
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

                if (attrTypeName == attributeName) {
                    var valueBytes = metadataReader.GetBlobBytes(attribute.Value);
                    var fileVersion = TryReadStringFromAttributeBlob(valueBytes);
                    if (fileVersion != null && Version.TryParse(fileVersion, out var parsedVersion)) {
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