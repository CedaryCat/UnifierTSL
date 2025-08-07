using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;


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
                var parameterTypes = ResolveConstructorParameterTypes(attr.Constructor, reader);

                // Read blob
                var blobReader = reader.GetBlobReader(attr.Value);

                // 3. Validate prolog
                if (blobReader.ReadUInt16() != 0x0001) {
                    parsedAttr = default;
                    return false;
                }

                // Read fixed constructor args
                var constructorArgs = new object?[parameterTypes.Count];
                for (int i = 0; i < parameterTypes.Count; i++) {
                    constructorArgs[i] = ReadFixedArg(ref blobReader, parameterTypes[i]);
                }

                // Read named arguments (fields/properties)
                var namedArgs = new Dictionary<string, object?>();
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
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                sigReader = reader.GetBlobReader(memberRef.Signature);
            }
            else if (ctorHandle.Kind == HandleKind.MethodDefinition) {
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
                sigReader = reader.GetBlobReader(methodDef.Signature);
            }
            else {
                throw new NotSupportedException($"Unsupported handle kind: {ctorHandle.Kind}");
            }

            var decoder = new SignatureDecoder<Type, object?>(new SimpleTypeProvider(), reader, genericContext: null);
            var methodSig = decoder.DecodeMethodSignature(ref sigReader);

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

        // Only decodes primitive types
        private class SimpleTypeProvider : ISignatureTypeProvider<Type, object?>
        {
            public Type GetPrimitiveType(PrimitiveTypeCode typeCode) =>
                typeCode switch {
                    PrimitiveTypeCode.Int32 => typeof(int),
                    PrimitiveTypeCode.String => typeof(string),
                    PrimitiveTypeCode.Boolean => typeof(bool),
                    PrimitiveTypeCode.Int16 => typeof(short),
                    PrimitiveTypeCode.Int64 => typeof(long),
                    PrimitiveTypeCode.Byte => typeof(byte),
                    PrimitiveTypeCode.Char => typeof(char),
                    PrimitiveTypeCode.Single => typeof(float),
                    PrimitiveTypeCode.Double => typeof(double),
                    PrimitiveTypeCode.Void => typeof(void),
                    _ => throw new NotSupportedException($"Primitive type {typeCode} not supported")
                };
            public Type GetArrayType(Type elementType, ArrayShape shape) => typeof(Array); // Not supported here
            public Type GetByReferenceType(Type elementType) => elementType;
            public Type GetFunctionPointerType(MethodSignature<Type> signature) => typeof(IntPtr);
            public Type GetGenericInstantiation(Type genericType, ImmutableArray<Type> typeArguments) => typeof(object);
            public Type GetGenericMethodParameter(object? genericContext, int index) => typeof(object);
            public Type GetGenericTypeParameter(object? genericContext, int index) => typeof(object);
            public Type GetModifiedType(Type modifier, Type unmodifiedType, bool isRequired) => unmodifiedType;
            public Type GetPinnedType(Type elementType) => elementType;
            public Type GetPointerType(Type elementType) => typeof(IntPtr);
            public Type GetSZArrayType(Type elementType) => elementType.MakeArrayType();
            public Type GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => typeof(object);

            public Type GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => typeof(object);
            public Type GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => typeof(object);
        }
    }
}
