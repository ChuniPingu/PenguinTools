using System.Text;
using PenguinTools.CLI;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var rootCommand = RootCommands.BuildRootCommand();
var parseResult = rootCommand.Parse(args);

if (parseResult.Errors.Count > 0)
{
    CliOutput.WriteParseErrors(parseResult.Errors.Select(error => error.Message));
    return CliExitCodes.SyntaxError;
}

return await parseResult.InvokeAsync();