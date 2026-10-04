namespace PenguinTools.Core.Metadata;

public partial record Meta
{
    public string BgmFilePath { get; set; } = string.Empty;
    public string FullBgmFilePath => GetFullPath(BgmFilePath);

    public decimal BgmRealOffset
    {
        get
        {
            if (!BgmEnableBarOffset)
            {
                return BgmManualOffset;
            }

            return BgmManualOffset + BgmBarOffset;
        }
    }

    public decimal BgmBarOffset
    {
        get
        {
            var beatsPerSecond = BgmInitialBpm / 60;
            var beatLength = 1 / beatsPerSecond;
            var measureLength = beatLength * BgmInitialNumerator;
            var fractionOfMeasure = measureLength * (4m / BgmInitialDenominator);
            return fractionOfMeasure;
        }
    }

    public decimal BgmManualOffset { get; set; }

    public bool BgmEnableBarOffset { get; set; }
    public decimal BgmInitialBpm { get; set; } = 120m;
    public int BgmInitialNumerator { get; set; } = 4;
    public int BgmInitialDenominator { get; set; } = 4;

    public decimal BgmPreviewStart { get; set; }
    public decimal BgmPreviewStop { get; set; }
}
