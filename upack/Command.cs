using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    public abstract class Command
    {
        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
        public sealed class PositionalArgumentAttribute : Attribute
        {
            public int Index { get; }
            public bool Optional { get; set; } = false;

            public PositionalArgumentAttribute(int index)
            {
                this.Index = index;
            }
        }

        [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
        public sealed class ExtraArgumentAttribute : Attribute
        {
            public bool Optional { get; set; } = true;
        }

        public sealed class PositionalArgument
        {
            private readonly PropertyInfo p;

            public int Index => p.GetCustomAttribute<PositionalArgumentAttribute>().Index;
            public bool Optional => p.GetCustomAttribute<PositionalArgumentAttribute>().Optional;
            public string DisplayName => p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name;
            public string Description => p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            public object DefaultValue => p.GetCustomAttribute<DefaultValueAttribute>()?.Value;

            internal PositionalArgument(PropertyInfo p)
            {
                this.p = p;
            }

            public string GetUsage()
            {
                var s = $"«{this.DisplayName}»";

                if (this.Optional)
                {
                    s = $"[{s}]";
                }

                return s;
            }

            public string GetHelp()
            {
                return $"{this.DisplayName} - {this.Description}";
            }

            public bool TrySetValue(Command cmd, string value)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class ExtraArgument
        {
            private readonly PropertyInfo p;

            public bool Optional => p.GetCustomAttribute<ExtraArgumentAttribute>().Optional;
            public string DisplayName => p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name;
            public string Description => p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            public object DefaultValue => p.GetCustomAttribute<DefaultValueAttribute>()?.Value;

            internal ExtraArgument(PropertyInfo p)
            {
                this.p = p;
            }

            public string GetUsage()
            {
                var s = $"--{this.DisplayName}=«{this.DisplayName}»";

                if (this.Optional)
                {
                    s = $"[{s}]";
                }

                if (p.PropertyType == typeof(bool) && this.DefaultValue == (object)false && this.Optional)
                {
                    s = $"[--{this.DisplayName}]";
                }

                return s;
            }

            public string GetHelp()
            {
                return $"{this.DisplayName} - {this.Description}";
            }

            public bool TrySetValue(Command cmd, string value)
            {
                throw new NotImplementedException();
            }
        }

        public string DisplayName => this.GetType().GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? this.GetType().Name;
        public string Description => this.GetType().GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        public IEnumerable<PositionalArgument> PositionalArguments => this.GetType().GetRuntimeProperties()
            .Where(p => p.GetCustomAttribute<PositionalArgumentAttribute>() != null)
            .Select(p => new PositionalArgument(p))
            .OrderBy(a => a.Index);

        public abstract Task<int> RunAsync();

        public IEnumerable<ExtraArgument> ExtraArguments => this.GetType().GetRuntimeProperties()
            .Where(p => p.GetCustomAttribute<ExtraArgumentAttribute>() != null)
            .Select(p => new ExtraArgument(p));

        public string GetUsage()
        {
            var s = new StringBuilder("upack ");

            s.Append(this.DisplayName);

            foreach (var arg in this.PositionalArguments)
            {
                s.Append(' ').Append(arg.GetUsage());
            }

            foreach (var arg in this.ExtraArguments)
            {
                s.Append(' ').Append(arg.GetUsage());
            }

            return s.ToString();
        }

        public string GetHelp()
        {
            var s = new StringBuilder("Usage: ");

            s.AppendLine(this.GetUsage()).AppendLine().AppendLine(this.Description);

            foreach (var arg in this.PositionalArguments)
            {
                s.AppendLine().Append(arg.GetHelp());
            }

            foreach (var arg in this.ExtraArguments)
            {
                s.AppendLine().Append(arg.GetHelp());
            }

            return s.ToString();
        }
    }
}