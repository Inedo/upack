using System.ComponentModel;
using System.Reflection;

namespace Inedo.UPack.CLI
{
    internal sealed class CommandDispatcher
    {
        private readonly Func<Command>[] commandFactories;

        public static CommandDispatcher Default => new();

        private CommandDispatcher()
        {
            this.commandFactories = new[]
            {
                f<Pack>(),
                f<Push>(),
                f<Unpack>(),
                f<Install>(),
                f<Update>(),
                f<Remove>(),
                f<List>(),
                f<Repack>(),
                f<Verify>(),
                f<Hash>(),
                f<Metadata>(),
                f<Get>(),
                f<Version>()
            };

            static Func<Command> f<TCommand>() where TCommand : Command, new() => () => new TCommand();
        }

        public async Task<int> MainAsync(string[] args)
        {
            bool onlyPositional = false;
            bool hadError = false;
            int exitCode = 0;

            var positional = new List<string>();
            var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var arg in args)
            {
                if (onlyPositional || !arg.StartsWith("--"))
                {
                    positional.Add(arg);
                }
                else if (arg == "--")
                {
                    onlyPositional = true;
                    continue;
                }
                else
                {
                    var parts = arg.Substring("--".Length).Split(new[] { '=' }, 2);
                    if (extra.ContainsKey(parts[0]))
                    {
                        hadError = true;
                    }

                    extra[parts[0]] = parts.Length == 1 ? null : parts[1];
                }
            }

            if (positional.Count > 0 && string.Equals("help", positional[0], StringComparison.OrdinalIgnoreCase))
            {
                hadError = true;
                positional.RemoveAt(0);
            }

            Command cmd = null;
            if (positional.Count == 0)
            {
                hadError = true;
            }
            else
            {
                foreach (var command in commandFactories)
                {
                    cmd = command();
                    if (!string.Equals(cmd.DisplayName, positional[0], StringComparison.OrdinalIgnoreCase))
                    {
                        cmd = null;
                        continue;
                    }

                    if (hadError)
                        break;

                    positional.RemoveAt(0);

                    foreach (var arg in cmd.PositionalArguments)
                    {
                        if (arg.Index < positional.Count)
                        {
                            if (!arg.TrySetValue(cmd, positional[arg.Index]))
                            {
                                hadError = true;
                            }
                        }
                        else if (arg.EnvironmentVariable != null)
                        {
                            var value = Environment.GetEnvironmentVariable(arg.EnvironmentVariable, EnvironmentVariableTarget.Process) ?? Environment.GetEnvironmentVariable(arg.EnvironmentVariable, EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable(arg.EnvironmentVariable, EnvironmentVariableTarget.Machine);
                            if (value == null && !arg.Optional)
                                hadError = true;

                            if (value != null)
                            {
                                if (!arg.TrySetValue(cmd, value))
                                    hadError = true;
                            }
                        }
                        else if (!arg.Optional)
                        {
                            hadError = true;
                        }
                    }

                    if (positional.Count > cmd.PositionalArguments.Count())
                        hadError = true;

                    foreach (var arg in cmd.ExtraArguments)
                    {
                        var alt = arg.AlternateNames.FirstOrDefault(extra.ContainsKey);
                        if (extra.ContainsKey(arg.DisplayName) || alt != null)
                        {
                            if (!arg.TrySetValue(cmd, extra[alt ?? arg.DisplayName]))
                            {
                                hadError = true;
                            }
                            extra.Remove(alt ?? arg.DisplayName);
                        }
                        else if (arg.EnvironmentVariable != null)
                        {
                            var value = Environment.GetEnvironmentVariable(arg.EnvironmentVariable, EnvironmentVariableTarget.Process) ?? Environment.GetEnvironmentVariable(arg.EnvironmentVariable, EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable(arg.EnvironmentVariable, EnvironmentVariableTarget.Machine);
                            if (value == null && !arg.Optional)
                                hadError = true;

                            if (value != null)
                            {
                                if (!arg.TrySetValue(cmd, value))
                                    hadError = true;
                            }
                        }
                        else if (!arg.Optional)
                        {
                            hadError = true;
                        }
                    }

                    if (extra.Count != 0)
                        hadError = true;

                    break;
                }
            }

            if (hadError || cmd == null)
            {
                if (cmd != null)
                    ShowHelp(cmd);
                else
                    ShowGenericHelp();

                exitCode = 2;
            }
            else
            {
                using var consoleCancelTokenSource = new CancellationTokenSource();

                Console.CancelKeyPress +=
                    (s, e) =>
                    {
                        consoleCancelTokenSource.Cancel();
                    };

                try
                {
                    try
                    {
                        exitCode = await cmd.RunAsync(consoleCancelTokenSource.Token);
                    }
                    catch (AggregateException ex) when (ex.InnerException is UpackException)
                    {
                        throw ex.InnerException;
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("Operation was canceled by the user.");
                    exitCode = 3;
                }
                catch (UpackException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    exitCode = 1;
                }
            }

            return exitCode;
        }

        private void ShowGenericHelp()
        {
            Console.Error.WriteLine($"upack {typeof(CommandDispatcher).Assembly.GetName().Version}");
            Console.Error.WriteLine("Usage: upack <command>");
            Console.Error.WriteLine();

            foreach (var f in commandFactories)
            {
                var command = f();
                var type = command.GetType();
                Console.Error.WriteLine($"{type.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? command.DisplayName} - {type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty}");
            }
        }

        private static void ShowHelp(Command cmd) => Console.Error.WriteLine(cmd.GetHelp());
    }
}
