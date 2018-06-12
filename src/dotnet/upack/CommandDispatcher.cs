using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    public sealed class CommandDispatcher
    {
        public static CommandDispatcher Default => new CommandDispatcher(typeof(Pack), typeof(Push), typeof(Unpack), typeof(Install), typeof(List), typeof(Repack), typeof(Verify), typeof(Hash));

        private readonly IEnumerable<Type> commands;

        public CommandDispatcher(params Type[] commands)
        {
            this.commands = commands;
        }

        public void Main(string[] args)
        {
            bool onlyPositional = false;
            bool hadError = false;

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
                foreach (var command in commands)
                {
                    cmd = (Command)command.GetConstructor(new Type[0]).Invoke(new object[0]);
                    if (!string.Equals(cmd.DisplayName, positional[0], StringComparison.OrdinalIgnoreCase))
                    {
                        cmd = null;
                        continue;
                    }

                    if (hadError)
                    {
                        break;
                    }

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
                        else if (!arg.Optional)
                        {
                            hadError = true;
                        }
                    }

                    if (positional.Count > cmd.PositionalArguments.Count())
                    {
                        hadError = true;
                    }

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
                        else if (!arg.Optional)
                        {
                            hadError = true;
                        }
                    }

                    if (extra.Count != 0)
                    {
                        hadError = true;
                    }

                    break;
                }
            }

            if (hadError || cmd == null)
            {
                if (cmd != null)
                {
                    ShowHelp(cmd);
                }
                else
                {
                    ShowGenericHelp();
                }
                Environment.ExitCode = 2;
            }
            else
            {
                using (var consoleCancelTokenSource = new CancellationTokenSource())
                {
                    Console.CancelKeyPress +=
                        (s, e) =>
                        {
                            consoleCancelTokenSource.Cancel();
                        };

                    try
                    {
                        try
                        {
                            Environment.ExitCode = cmd.RunAsync(consoleCancelTokenSource.Token).GetAwaiter().GetResult();
                        }
                        catch (AggregateException ex) when (ex.InnerException is UpackException)
                        {
                            throw ex.InnerException;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        Console.Error.WriteLine("Operation was canceled by the user.");
                        Environment.ExitCode = 3;
                    }
                    catch (UpackException ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                        Environment.ExitCode = 1;
                    }
                }
            }
        }

        public void ShowGenericHelp()
        {
            Console.Error.WriteLine($"upack {typeof(CommandDispatcher).Assembly.GetName().Version}");
            Console.Error.WriteLine("Usage: upack «command»");
            Console.Error.WriteLine();

            foreach (var command in commands)
            {
                Console.Error.WriteLine($"{command.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? command.Name} - {command.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty}");
            }
        }

        public void ShowHelp(Command cmd)
        {
            Console.Error.WriteLine(cmd.GetHelp());
        }
    }
}
