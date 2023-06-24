using System;

namespace KalmiaZero.Utils
{
    public class GameTimerOptions : ICloneable
    {
        public int MainTimeMs { get; set; }
        public int ByoyomiMs { get; set; }
        public int IncrementMs { get; set; }
        public int ByoyomiStones { get; set; }

        public object Clone() => new GameTimerOptions
        {
            MainTimeMs = this.MainTimeMs,
            ByoyomiMs = this.ByoyomiMs,
            IncrementMs = this.IncrementMs,
            ByoyomiStones = this.ByoyomiStones
        };
    }

    public class GameTimer
    {
        public bool IsTicking { get; private set; }
        public bool Timeout { get; private set; }

        public int MainTimeMs 
        {
            get => this.mainTimeMs;

            set
            {
                if (this.IsTicking)
                    throw new InvalidOperationException("Changing main time is invalid while ticking.");

                this.mainTimeMs = value;
                this.timeLeftMs = value + this.byoyomiMs;
            }
        }

        public int ByoyomiMs 
        {
            get => this.byoyomiMs;

            set
            {
                if (this.IsTicking)
                    throw new InvalidOperationException("Changing byoyomi is invalid while ticking.");

                if(this.timeLeftMs <= this.byoyomiMs)
                {
                    this.byoyomiMs = value;
                    this.timeLeftMs = value;
                    return;
                }

                this.timeLeftMs -= this.byoyomiMs;
                this.byoyomiMs = value;
                this.timeLeftMs += value;
            } 
        }

        public int ByoyomiStones 
        {
            get => this.byoyomiStones;

            set
            {
                if (this.IsTicking)
                    throw new InvalidOperationException("Changing byoyomi stones is invalid while ticking.");

                this.byoyomiStones = value;
            } 
        }

        public int IncrementMs 
        {
            get => this.incrementMs;

            set
            {
                if (this.IsTicking)
                    throw new InvalidOperationException("Changing increment is invalid while ticking.");

                this.incrementMs = value;
            } 
        }

        public int TimeLeftMs 
        {
            get
            {
                if(!this.IsTicking)
                    return this.timeLeftMs;

                var prevCheckPointMs = this.checkPointMs;
                this.checkPointMs = Environment.TickCount;
                this.timeLeftMs -= this.checkPointMs - prevCheckPointMs;
                return Math.Max(0, this.timeLeftMs);
            }
        }

        public int MainTimeLeftMs
        {
            get
            {
                var timeLeftMs = this.TimeLeftMs;
                return (timeLeftMs <= this.byoyomiMs) ? 0 : timeLeftMs -= this.byoyomiMs;
            }

            set
            {
                if (this.IsTicking)
                    throw new InvalidOperationException("Main time left cannot be set while ticking.");

                if (value > this.MainTimeMs)
                    throw new ArgumentOutOfRangeException(nameof(value), "Main time left cannot be greater than main time.");

                this.timeLeftMs = value + this.byoyomiMs;
            }
        }

        public int ByoyomiLeft
        {
            get
            {
                if (this.MainTimeLeftMs != 0)
                    return this.byoyomiMs;
                else
                    return this.timeLeftMs;
            }
        }

        public int ByoyomiStonesLeft 
        {
            get => this.byoyomiStonesLeft;

            set
            {
                if (this.IsTicking)
                    throw new InvalidOperationException("Byoyomi stones left cannot be set while ticking.");

                if (value > this.MainTimeMs)
                    throw new ArgumentOutOfRangeException(nameof(value), "Byoyomi stones left cannot be greater than byoyomi stones.");

                this.byoyomiStonesLeft = value;
            } 
        }

        int mainTimeMs;
        int byoyomiMs;
        int byoyomiStones;
        int incrementMs;
        int byoyomiStonesLeft;
        int timeLeftMs;     // main time left + byoyomi
        int checkPointMs;

        public GameTimer(GameTimerOptions options)
        {
            this.MainTimeMs = options.MainTimeMs;
            this.ByoyomiMs = options.ByoyomiMs;
            this.ByoyomiStones = options.ByoyomiStones;
            this.IncrementMs = options.IncrementMs;
            this.ByoyomiStonesLeft = this.ByoyomiStones;
            this.timeLeftMs = this.MainTimeMs + this.ByoyomiMs;
        }

        public void Start()
        {
            if (this.IsTicking)
                throw new InvalidOperationException("Time has already started.");

            this.checkPointMs = Environment.TickCount;
            this.IsTicking = true;
        }

        public void Stop()
        {
            if (!this.IsTicking)
                throw new InvalidOperationException("Timer has already stopped.");

            var ellapsedMs = Environment.TickCount - this.checkPointMs; 
            this.timeLeftMs -= ellapsedMs;
            this.IsTicking = false;

            if(this.timeLeftMs < 0)
            {
                this.timeLeftMs = 0;
                this.Timeout = true;
                return;
            }

            if(this.timeLeftMs < this.byoyomiMs)
                if (--this.byoyomiStonesLeft == 0)
                {
                    this.byoyomiStonesLeft = this.byoyomiStones;
                    this.timeLeftMs = this.byoyomiMs;
                }

            this.timeLeftMs += this.IncrementMs;
        }

        public void Reset()
        {
            this.IsTicking = this.Timeout = false;
            this.timeLeftMs = this.mainTimeMs + this.byoyomiMs;
            this.byoyomiStonesLeft = this.byoyomiStones;
        }
    }
}
