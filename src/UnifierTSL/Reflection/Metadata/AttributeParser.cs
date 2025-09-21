using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using UnifierTSL.Reflection.Metadata.DecodeProviders;


namespace UnifierTSL.Reflection.Metadata
{
    public readonly struct ParsedCustomAttribute
    {
        public object?[] ConstructorArguments { get; init; }
        public Dictionary<string, object?> NamedArguments { get; init; }
    }
    public static class AttributeParser
    {
        public static bool TryParseCustomAttribute(CustomAttribute attr, MetadataReader reader, out ParsedCustomAttribute parsedAttr) {
            try {
                // Resolve constructor parameter types
                List<Type> parameterTypes = ResolveConstructorParameterTypes(attr.Constructor, reader);

                // Read blob
                BlobReader blobReader = reader.GetBlobReader(attr.Value);

                // 3. Validate prolog
                if (blobReader.ReadUInt16() != 0x0001) {
                    parsedAttr = default;
                    return false;
                }

                // Read fixed constructor args
                object?[] constructorArgs = new object?[parameterTypes.Count];
                for (int i = 0; i < parameterTypes.Count; i++) {
                    constructorArgs[i] = ReadFixedArg(ref blobReader, parameterTypes[i]);
                }

                // Read named arguments (fields/properties)
                Dictionary<string, object?> namedArgs = [];
                ushort namedCount = blobReader.ReadUInt16();

                for (int i = 0; i < namedCount; i++) {
                    byte kind = blobReader.ReadByte(); // 0x53 = field, 0x54 = property
                    byte typeCode = blobReader.ReadByte(); // ElementType

                    Type type = GetTypeFromElementType(typeCode);

                    string name = blobReader.ReadSerializedString()!;
                    object? value = ReadFixedArg(ref blobReader, type);
                    namedArgs[name] = value;
                }

                parsedAttr = new ParsedCustomAttribute {
                    ConstructorArguments = constructorArgs,
                    NamedArguments = namedArgs
                };

                return true;
            }
            catch {
                parsedAttr = default;
                return false;
            }
        }

        private static List<Type> ResolveConstructorParameterTypes(EntityHandle ctorHandle, MetadataReader reader) {
            BlobReader sigReader;

            if (ctorHandle.Kind == HandleKind.MemberReference) {
                MemberReference memberRef = reader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                sigReader = reader.GetBlobReader(memberRef.Signature);
            }
            else if (ctorHandle.Kind == HandleKind.MethodDefinition) {
                MethodDefinition methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
                sigReader = reader.GetBlobReader(methodDef.Signature);
            }
            else {
                throw new NotSupportedException($"Unsupported handle kind: {ctorHandle.Kind}");
            }

            SignatureDecoder<Type, object?> decoder = new(new SimpleTypeProvider(), reader, genericContext: null);
            MethodSignature<Type> methodSig = decoder.DecodeMethodSignature(ref sigReader);

            return [.. methodSig.ParameterTypes];
        }

        private static object? ReadFixedArg(ref BlobReader reader, Type type) {
            if (type == typeof(int)) return reader.ReadInt32();
            if (type == typeof(string)) return reader.ReadSerializedString();
            if (type == typeof(bool)) return reader.ReadBoolean();
            if (type == typeof(byte)) return reader.ReadByte();
            if (type == typeof(short)) return reader.ReadInt16();
            if (type == typeof(long)) return reader.ReadInt64();
            if (type == typeof(float)) return reader.ReadSingle();
            if (type == typeof(double)) return reader.ReadDouble();
            if (type == typeof(char)) return (char)reader.ReadUInt16();

            // Simple enum support (read as underlying int)
            if (type.IsEnum)
                return reader.ReadInt32();

            // Fallback
            throw new NotSupportedException($"Unsupported fixed argument type: {type}");
        }

        private static Type GetTypeFromElementType(byte typeCode) {
            return typeCode switch {
                0x02 => typeof(bool),
                0x03 => typeof(char),
                0x04 => typeof(sbyte),
                0x05 => typeof(byte),
                0x06 => typeof(short),
                0x07 => typeof(ushort),
                0x08 => typeof(int),
                0x09 => typeof(uint),
                0x0a => typeof(long),
                0x0b => typeof(ulong),
                0x0c => typeof(float),
                0x0d => typeof(double),
                0x0e => typeof(string),
                _ => throw new NotSupportedException($"Unsupported ElementType code: 0x{typeCode:X2}")
            };
        }
    }
}
