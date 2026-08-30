using System;

namespace MobiusCli
{
    public static class Program
    {
        public static int Main(string[] args) => Cli.Run(args, Console.Out, Console.Error);
    }
}
