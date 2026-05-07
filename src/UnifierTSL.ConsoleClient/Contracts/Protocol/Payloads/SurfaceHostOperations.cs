namespace UnifierTSL.Contracts.Protocol.Payloads {
    public static class SurfaceHostOperations {
        public static SurfaceHostOperation SetTitle(string title) {
            ArgumentNullException.ThrowIfNull(title);
            return new SurfaceHostPropertiesPatchOperation {
                Title = title,
            };
        }

        public static SurfaceHostOperation SetSize(int width = 0, int height = 0) {
            return new SurfaceHostPropertiesPatchOperation {
                Width = width == 0 ? null : width,
                Height = height == 0 ? null : height,
            };
        }

        public static SurfaceHostOperation SetPosition(int left = 0, int top = 0) {
            return new SurfaceHostPropertiesPatchOperation {
                Left = left == 0 ? null : left,
                Top = top == 0 ? null : top,
            };
        }

        public static SurfaceHostOperation SetInputEncoding(string encodingName) {
            ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);
            return new SurfaceHostPropertiesPatchOperation {
                InputEncoding = encodingName,
            };
        }

        public static SurfaceHostOperation SetOutputEncoding(string encodingName) {
            ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);
            return new SurfaceHostPropertiesPatchOperation {
                OutputEncoding = encodingName,
            };
        }

        public static SurfaceHostOperation PropertiesPatch(SurfaceHostPropertiesPatchOperation properties) {
            ArgumentNullException.ThrowIfNull(properties);
            return new SurfaceHostPropertiesPatchOperation {
                Title = properties.Title,
                Width = properties.Width,
                Height = properties.Height,
                Left = properties.Left,
                Top = properties.Top,
                InputEncoding = properties.InputEncoding,
                OutputEncoding = properties.OutputEncoding,
            };
        }

        public static SurfaceHostOperation Clear() {
            return new SurfaceHostClearOperation();
        }

        public static bool TryGetProperties(SurfaceHostOperation operation, out SurfaceHostPropertiesPatchOperation properties) {
            ArgumentNullException.ThrowIfNull(operation);
            if (operation is SurfaceHostPropertiesPatchOperation typed) {
                properties = typed;
                return true;
            }

            properties = new SurfaceHostPropertiesPatchOperation();
            return false;
        }

        public static bool IsClear(SurfaceHostOperation operation) {
            return operation is SurfaceHostClearOperation;
        }

        public static SurfaceHostPropertiesPatchOperation MergeProperties(
            SurfaceHostPropertiesPatchOperation current,
            SurfaceHostPropertiesPatchOperation patch) {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(patch);
            return new SurfaceHostPropertiesPatchOperation {
                Title = patch.Title ?? current.Title,
                Width = patch.Width ?? current.Width,
                Height = patch.Height ?? current.Height,
                Left = patch.Left ?? current.Left,
                Top = patch.Top ?? current.Top,
                InputEncoding = patch.InputEncoding ?? current.InputEncoding,
                OutputEncoding = patch.OutputEncoding ?? current.OutputEncoding,
            };
        }

        public static bool HasProperties(SurfaceHostPropertiesPatchOperation properties) {
            return properties.Title is not null
                || properties.Width is not null
                || properties.Height is not null
                || properties.Left is not null
                || properties.Top is not null
                || properties.InputEncoding is not null
                || properties.OutputEncoding is not null;
        }
    }
}
