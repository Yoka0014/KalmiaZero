using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KalmiaZero.Engines
{
    public enum EngineOptionType
    {
        Check,
        Spin,
        Combo,
        Button,
        String,
        FileName
    }

    public class EngineOption
    {
        public dynamic DefaultValue { get; }
        public IComparable? MinValue { get; } 
        public IComparable? MaxValue { get; }
        public ReadOnlyCollection<dynamic> ComboItems => new(this.comboItems);
        public EngineOptionType Type { get; }

        public dynamic CurrentValue
        {
            get => this.currentValue;

            private set
            {
                if (this.MinValue is not null)
                    if (((IComparable)this.currentValue).CompareTo(this.MinValue) < 0)
                        return;

                if (this.MaxValue is not null)
                    if (((IComparable)this.currentValue).CompareTo(this.MaxValue) > 0)
                        return;

                if (this.Type == EngineOptionType.Combo && !this.comboItems.Contains(value))
                    return;

                this.currentValue = value;
                this.ValueChanged.Invoke(this, value);
            }
        }

        public string CurrentValueString 
        {
            get => this.currentValue.ToString();

            set
            {
                if (this.Type == EngineOptionType.Button)
                    return;

                (bool isValid, dynamic v) = this.parser.Invoke(value);
                if (isValid)
                    this.CurrentValue = v;
            }
        }

        public event EventHandler<dynamic> ValueChanged = delegate { };

        dynamic currentValue;
        Func<string, (bool isValid, dynamic value)> parser;
        readonly List<dynamic> comboItems = new();

        public EngineOption(bool value) 
            : this(value, EngineOptionType.Check, str => (bool.TryParse(str, out bool res), res)) { }

        public EngineOption(string value, EngineOptionType type) 
            : this(value, type, str=>(true, str)) { }

        public EngineOption(long value, long minValue, long maxValue) 
            : this(value, minValue, maxValue, EngineOptionType.Spin, str => (long.TryParse(str, out long res), res)) { }

        public EngineOption(int defaultIdx, IEnumerable<dynamic> comboItems, Func<string, (bool isValid, dynamic value)> parser)
        {
            this.Type = EngineOptionType.Combo;
            this.comboItems.AddRange(comboItems);
            this.DefaultValue = this.comboItems[defaultIdx];
            this.MinValue = this.MaxValue = null;
            this.currentValue = this.DefaultValue;
            this.parser = parser;
        }

        public EngineOption(dynamic value, EngineOptionType type, Func<string, (bool isValid, dynamic value)> parser)
        {
            this.DefaultValue = value;
            this.MinValue = this.MaxValue = null;
            this.Type = type;
            this.currentValue = value;
            this.parser = parser;
        }

        public EngineOption(dynamic value, IComparable minValue, IComparable maxValue, EngineOptionType type,
                            Func<string, (bool isValid, dynamic value)> parser)
        {
            this.DefaultValue = value;
            this.MinValue = minValue;
            this.MaxValue = maxValue;
            this.Type = type;
            this.currentValue = value;
            this.parser = parser;
        }
    }
}
