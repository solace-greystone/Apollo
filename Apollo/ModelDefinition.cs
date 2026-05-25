using Whisper.net.Ggml;

namespace Apollo {
    public sealed class ModelDefinition {
        public GgmlType GgmlType { get; }
        public string FileName { get; }
        public string DisplayName { get; }
        public int ApproxSizeMb { get; }

        public ModelDefinition(GgmlType ggmlType, string fileName, string displayName, int approxSizeMb) {
            GgmlType = ggmlType;
            FileName = fileName;
            DisplayName = displayName;
            ApproxSizeMb = approxSizeMb;
        }
    }

    public static class ModelCatalog {
        public static readonly ModelDefinition BaseEn = new(
            GgmlType.BaseEn, "ggml-base.en.bin", "Base (English)", 142);

        public static readonly ModelDefinition SmallEn = new(
            GgmlType.SmallEn, "ggml-small.en.bin", "Small (English)", 466);

        public static readonly ModelDefinition MediumEn = new(
            GgmlType.MediumEn, "ggml-medium.en.bin", "Medium (English)", 1500);

        public static readonly ModelDefinition Default = MediumEn;

        public static readonly ModelDefinition[] All = { BaseEn, SmallEn, MediumEn };
    }
}
