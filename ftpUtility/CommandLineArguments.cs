using CommandLine;

namespace ndFTP
{
    internal class CommandLineArguments
    {
        [Option('e', "encrypt", Default = false, Required = false, HelpText = "Passwortverschlüsselung")]
        public bool encrypt { get; set; }

    }
}
