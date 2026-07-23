using System.CommandLine;
using System.Text;

namespace PenguinTools.CRI;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var root = new RootCommand("CRI HCA/AWB/ACB conversion utility");

        var convert = new Command("convert", "Encode a normalized WAV and patch a dummy ACB template");
        var wavOption = new Option<FileInfo>("--wav") { Required = true };
        var acbOption = new Option<FileInfo>("--acb") { Required = true };
        var awbOption = new Option<FileInfo>("--awb") { Required = true };
        var nameOption = new Option<string>("--name") { Required = true };
        var previewStartOption = new Option<long>("--preview-start-ms") { Required = true };
        var previewStopOption = new Option<long>("--preview-stop-ms") { Required = true };
        var hcaKeyConvertOption = new Option<ulong>("--hca-key")
        {
            DefaultValueFactory = _ => ConvertService.DefaultHcaKey
        };
        var bitrateOption = new Option<uint>("--bitrate")
        {
            DefaultValueFactory = _ => ConvertService.DefaultBitrate
        };
        convert.Options.Add(wavOption);
        convert.Options.Add(acbOption);
        convert.Options.Add(awbOption);
        convert.Options.Add(nameOption);
        convert.Options.Add(previewStartOption);
        convert.Options.Add(previewStopOption);
        convert.Options.Add(hcaKeyConvertOption);
        convert.Options.Add(bitrateOption);
        convert.SetAction(parseResult =>
        {
            try
            {
                ConvertService.Convert(
                    parseResult.GetValue(wavOption)!.FullName,
                    parseResult.GetValue(acbOption)!.FullName,
                    parseResult.GetValue(awbOption)!.FullName,
                    parseResult.GetValue(nameOption)!,
                    parseResult.GetValue(previewStartOption),
                    parseResult.GetValue(previewStopOption),
                    parseResult.GetValue(hcaKeyConvertOption),
                    parseResult.GetValue(bitrateOption));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        var extract = new Command("extract", "Decode every cue in an ACB or AWB to PCM WAV");
        var sourceArg = new Argument<FileInfo>("source");
        var outputArg = new Argument<DirectoryInfo>("output");
        var pairedOption = new Option<FileInfo?>("--paired-input");
        var hcaKeyExtractOption = new Option<ulong>("--hca-key")
        {
            DefaultValueFactory = _ => ConvertService.DefaultHcaKey
        };
        extract.Arguments.Add(sourceArg);
        extract.Arguments.Add(outputArg);
        extract.Options.Add(pairedOption);
        extract.Options.Add(hcaKeyExtractOption);
        extract.SetAction(parseResult =>
        {
            try
            {
                var paired = parseResult.GetValue(pairedOption);
                var manifest = ExtractService.Extract(
                    parseResult.GetValue(sourceArg)!.FullName,
                    parseResult.GetValue(outputArg)!.FullName,
                    paired?.FullName,
                    parseResult.GetValue(hcaKeyExtractOption));
                Console.WriteLine(ExtractService.SerializeManifest(manifest));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        root.Subcommands.Add(convert);
        root.Subcommands.Add(extract);
        var parseResult = root.Parse(args);
        return await parseResult.InvokeAsync();
    }
}
