using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace KalmiaZero.Learn
{
    public record class TDGAConfig<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public TDTrainerConfig<WeightType> TDConfig { get; init; }
        public GAConfig<WeightType> GAConfig { get; init; }
        public SupervisedTrainerConfig<WeightType> SLConfig { get; init; }

        public int RoundRobinInterval { get; init; } = 100;
        public int NumIterations { get; init; } = 2;
        public int NTupleSize { get; init; } = 10;
        public int NumNTuples { get; init; } = 12;
        public int NumThreads { get; init; } = Environment.ProcessorCount;
        public Random Random { get; init; } = new(Random.Shared.Next());

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string PoolFileName { get; init; } = "pool";
        public string FitnessHistoryFileName { get; init; } = "fitness_history";
    }

    public class TDGA<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        readonly TDGAConfig<WeightType> CONFIG;
        readonly string POOL_FILE_NAME;
        readonly string FITNESS_HISTROY_FILE_NAME;
        readonly int NTUPLE_SIZE;
        readonly int NUM_NTUPLES;
        readonly Random RAND;
        readonly ParallelOptions PARALLEL_OPTIONS;

        public TDGA(TDGAConfig<WeightType> config)
        {
            this.CONFIG = config;
            this.NTUPLE_SIZE = config.NTupleSize;
            this.NUM_NTUPLES = config.NumNTuples;

            this.POOL_FILE_NAME = $"{config.PoolFileName}_{"{0}"}";
            this.FITNESS_HISTROY_FILE_NAME = $"{config.FitnessHistoryFileName}_{"{0}"}";
            this.RAND = config.Random;
            this.PARALLEL_OPTIONS = new ParallelOptions { MaxDegreeOfParallelism = config.NumThreads };
        }

        void Train(string poolPath, int numGenerations)
        {

        }
    }
}
