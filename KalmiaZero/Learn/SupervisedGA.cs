using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using KalmiaZero.Utils;
using KalmiaZero.Reversi;
using System.Numerics;
using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using System.Threading;

namespace KalmiaZero.Learn
{
    public record class SupervisedGAConfig<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        public int PopulationSize { get; init; } = 100;
        public double EliteRate { get; init; } = 0.2;
        public double MutantRate { get; init; } = 0.2;
        public double EliteInheritanceProb { get; init; } = 0.7;
        public int NTupleSize { get; init; } = 7;
        public int NumNTuples { get; init; } = 12;
        public SupervisedTrainerConfig<WeightType> SLConfig { get; init; } = new() { NumEpoch = 20 };
        public int NumThreads { get; init; } = Environment.ProcessorCount;
        public Random Random { get; init; } = new (Random.Shared.Next());

        public string WorkDir { get; init; } = Environment.CurrentDirectory;
        public string PoolFileName { get; init; } = "pool";
        public string FitnessHistroyFileName { get; init; } = "fitness_histroy";
    }

    public class SupervisedGA<WeightType> where WeightType : unmanaged, IFloatingPointIeee754<WeightType>
    {
        readonly SupervisedGAConfig<WeightType> CONFIG;
        readonly SupervisedTrainerConfig<WeightType> SL_CONFIG;
        readonly string POOL_FILE_PATH;
        readonly string FITNESS_HISTROY_FILE_PATH;
        readonly int POPULATION_SIZE;
        readonly int NUM_ELITES;
        readonly int NUM_MUTANTS;
        readonly double ELITE_INHERITANCE_PROB;
        readonly int NTUPLE_SIZE;
        readonly int NUM_NTUPLES;
        readonly Random RAND;
        readonly ParallelOptions PARALLEL_OPTIONS;

        Indivisual[] pool;
        Indivisual[] nextPool;

        readonly List<(float best, float worst, float median, float average)> fitnessHistory = new();

        public SupervisedGA(SupervisedGAConfig<WeightType> config) 
        {
            this.CONFIG = config;
            this.SL_CONFIG = config.SLConfig;
            this.POOL_FILE_PATH = $"{config.WorkDir}/{config.PoolFileName}_{"{0}"}.bin";
            this.FITNESS_HISTROY_FILE_PATH = $"{config.WorkDir}/{config.FitnessHistroyFileName}.txt";

            this.POPULATION_SIZE = config.PopulationSize;
            this.NUM_ELITES = (int)(this.POPULATION_SIZE * config.EliteRate);
            this.NUM_MUTANTS = (int)(this.POPULATION_SIZE * config.MutantRate);
            this.ELITE_INHERITANCE_PROB = config.EliteInheritanceProb;
            this.NTUPLE_SIZE = config.NTupleSize;
            this.NUM_NTUPLES = config.NumNTuples;
            this.RAND = config.Random;
            this.PARALLEL_OPTIONS = new ParallelOptions { MaxDegreeOfParallelism = config.NumThreads };
            
            this.pool = new Indivisual[this.POPULATION_SIZE];
            this.nextPool = new Indivisual[this.POPULATION_SIZE];
        }

        public void Train(TrainData[] evalData, int numGenerations)
            => Train(Enumerable.Range(0, POPULATION_SIZE).Select(_ => new Indivisual(Constants.NUM_SQUARES * this.NUM_NTUPLES, CONFIG.Random)).ToArray(), evalData, numGenerations);

        public void Train(string poolPath, TrainData[] evalData, int numGenerations)
            => Train(Indivisual.LoadPoolFromFile(poolPath), evalData, numGenerations);

        void Train(Indivisual[] initialPool, TrainData[] evalData, int numGenerations)
        {
            Array.Copy(initialPool, this.pool, this.pool.Length);
            Array.Copy(initialPool, this.nextPool, this.nextPool.Length);

            for (var gen = 0; gen < numGenerations; gen++)
            {
                Console.WriteLine($"Generation: {gen}");

                EvaluatePool(evalData);

                this.fitnessHistory.Add((this.pool[0].Fitness, this.pool[^1].Fitness, this.pool[this.pool.Length / 2].Fitness, this.pool.Average(p => p.Fitness)));

                var (best, worst, median, average) = this.fitnessHistory[^1];
                Console.WriteLine($"\nBestFitness: {best}");
                Console.WriteLine($"WorstFitness: {worst}");
                Console.WriteLine($"MedianFitness: {median}");
                Console.WriteLine($"AverageFitness: {average}");

                Indivisual.SavePoolAt(this.pool, string.Format(POOL_FILE_PATH, gen));
                SaveFitnessHistory();

                Console.WriteLine("Generate indivisuals for next generation.");
                var elites = this.pool.AsSpan(0, this.NUM_ELITES);
                var nonElites = this.pool.AsSpan(this.NUM_ELITES);
                elites.CopyTo(this.nextPool);

                GenerateMutants();
                GenerateChildren(ref elites, ref nonElites);

                (this.pool, this.nextPool) = (this.nextPool, this.pool);
                Console.WriteLine();
            }
        }

        void SaveFitnessHistory()
        {
            var bestSb = new StringBuilder("[");
            var worstSb = new StringBuilder("[");
            var medianSb = new StringBuilder("[");
            var averageSb = new StringBuilder("[");
            foreach((var best, var worst, var median, var average) in this.fitnessHistory)
            {
                bestSb.Append(best).Append(", ");
                worstSb.Append(worst).Append(", ");
                medianSb.Append(median).Append(", ");
                averageSb.Append(average).Append(", ");
            }

            // remove last ", ";
            bestSb.Remove(bestSb.Length - 2, 2);
            worstSb.Remove(worstSb.Length - 2, 2);
            medianSb.Remove(medianSb.Length - 2, 2);
            averageSb.Remove(averageSb.Length - 2, 2);

            bestSb.Append(']');
            worstSb.Append(']');
            medianSb.Append(']');
            averageSb.Append(']');

            using var sw = new StreamWriter(this.FITNESS_HISTROY_FILE_PATH);
            sw.WriteLine(bestSb.ToString());
            sw.WriteLine(worstSb.ToString());
            sw.WriteLine(medianSb.ToString());
            sw.WriteLine(averageSb.ToString());
        }

        void EvaluatePool(TrainData[] evalData)
        {
            Console.WriteLine("Start evaluation.");
            var count = 0;
            Parallel.For(0, this.pool.Length, this.PARALLEL_OPTIONS, i =>
            {
                if (float.IsNegativeInfinity(this.pool[i].Fitness))
                    EvaluateIndivisual(ref this.pool[i], evalData, i);
                Interlocked.Increment(ref count);
                Console.WriteLine($"{count} indivisuals were evaluated({count * 100.0 / this.POPULATION_SIZE:f2}%).");
            });
            Array.Sort(this.pool);
        }

        void GenerateMutants()
        {
            var mutants = this.nextPool.AsSpan(this.NUM_ELITES, this.NUM_MUTANTS);
            for (var i = 0; i < mutants.Length; i++)
            {
                ref var mutant = ref mutants[i];
                for (var j = 0; j < mutant.Chromosome.Length; j++)
                    mutant.Chromosome[j] = this.RAND.NextSingle();
                mutant.Fitness = float.NegativeInfinity;
            }
        }

        void GenerateChildren(ref Span<Indivisual> elites, ref Span<Indivisual> nonElites)
        {
            var children = this.nextPool.AsSpan(this.NUM_ELITES + this.NUM_MUTANTS);
            for (var i = 0; i < children.Length; i++)
                Crossover(ref elites[this.RAND.Next(elites.Length)], ref nonElites[this.RAND.Next(nonElites.Length)], ref children[i]);
        }

        void Crossover(ref Indivisual eliteParent, ref Indivisual nonEliteParent, ref Indivisual child)
        {
            (var eliteChrom, var nonEliteChrom) = (eliteParent.Chromosome, nonEliteParent.Chromosome);
            var childChrom = child.Chromosome;
            for(var i = 0; i < childChrom.Length; i++)
                childChrom[i] = (this.RAND.NextDouble() < this.ELITE_INHERITANCE_PROB) ? eliteChrom[i] : nonEliteChrom[i];
            child.Fitness = float.NegativeInfinity;
        }

        void EvaluateIndivisual(ref Indivisual indivisual, TrainData[] evalData, int id)
        {
            var nTuples = new NTuples(DecodeChromosome(indivisual.Chromosome, this.NTUPLE_SIZE, this.NUM_NTUPLES));
            var valueFunc = new ValueFunction<WeightType>(nTuples);
            var slTrainer = new SupervisedTrainer<WeightType>($"INDV_{id}", valueFunc, this.SL_CONFIG, Stream.Null);
            var loss = slTrainer.Train(evalData, Array.Empty<TrainData>(), saveWeights: false, saveLossHistroy: false).trainLoss;
            indivisual.Fitness = 1.0f / float.CreateChecked(loss);
        }

        public static NTuples[] DecodePool(string poolPath, int nTupleSize, int numNTuples, int numIndivisual=-1)
        {
            var pool = Indivisual.LoadPoolFromFile(poolPath);
            Array.Sort(pool);
            if (numIndivisual == -1)
                numIndivisual = pool.Length;

            var nTuplesSet = new NTuples[numIndivisual];
            for (var i = 0; i < nTuplesSet.Length; i++)
                nTuplesSet[i] = new NTuples(DecodeChromosome(pool[i].Chromosome, nTupleSize, numNTuples));

            return nTuplesSet;
        }

        static NTupleInfo[] DecodeChromosome(float[] chromosome, int nTupleSize, int numNTuples)
        {
            var nTuples = new NTupleInfo[numNTuples];
            var coords = new BoardCoordinate[nTupleSize];
            var adjCoords = new List<BoardCoordinate>();
            for (var nTupleID = 0; nTupleID < numNTuples; nTupleID++)
            {
                var chrom = chromosome.AsSpan(nTupleID * Constants.NUM_SQUARES, Constants.NUM_SQUARES);
                var min = chrom[0];
                var minIdx = 0;
                for (var i = 1; i < chrom.Length; i++)
                {
                    if (chrom[i] < min)
                    {
                        minIdx = i;
                        min = chrom[i];
                    }
                }

                Array.Fill(coords, BoardCoordinate.Null);
                coords[0] = (BoardCoordinate)minIdx;
                adjCoords.Clear();
                for(var i = 1; i < nTupleSize; i++)
                {
                    adjCoords.AddRange(Reversi.Utils.GetAdjacent8Squares(coords[i - 1]));
                    adjCoords.RemoveAll(coords[..i].Contains);

                    min = float.PositiveInfinity;
                    foreach (var adjCoord in adjCoords)
                    {
                        if (chrom[(int)adjCoord] < min)
                        {
                            coords[i] = adjCoord;
                            min = chrom[(int)adjCoord];
                        }
                    }

                    if (float.IsPositiveInfinity(min))
                        throw new ArgumentException($"Cannot create a {nTupleSize}-Tuple from the specified chromosome.");
                }

                nTuples[nTupleID] = new NTupleInfo(coords);
            }

            return nTuples;
        }

        struct Indivisual : IComparable<Indivisual> 
        {
            const string LABEL = "KalmiaZero_Pool";
            const string LABEL_INVERSED = "looP_oreZaimlaK";
            const int LABEL_SIZE = 15;

            public float[] Chromosome;
            public float Fitness;
            
            public Indivisual(int chromosomeSize) : this(chromosomeSize, Random.Shared) { }

            public Indivisual(int chromosomeSize, Random rand)
            {
                this.Chromosome = Enumerable.Range(0, chromosomeSize).Select(_ => rand.NextSingle()).ToArray();
                this.Fitness = float.NegativeInfinity;
            }

            public Indivisual(Stream stream, bool swapBytes)
            {
                const int BUFFER_SIZE = 4;

                Span<byte> buffer = stackalloc byte[BUFFER_SIZE];
                stream.Read(buffer[..sizeof(int)], swapBytes);
                this.Chromosome = new float[BitConverter.ToInt32(buffer)];
                for(var i = 0; i < this.Chromosome.Length; i++)
                {
                    stream.Read(buffer[..sizeof(float)], swapBytes);
                    this.Chromosome[i] = BitConverter.ToSingle(buffer);
                }

                stream.Read(buffer[..sizeof(float)], swapBytes);
                this.Fitness = BitConverter.ToSingle(buffer);
            }

            public readonly void WriteTo(Stream stream)
            {
                stream.Write(BitConverter.GetBytes(this.Chromosome.Length));
                foreach (var gene in this.Chromosome)
                    stream.Write(BitConverter.GetBytes(gene));
                stream.Write(BitConverter.GetBytes(this.Fitness));
            }

            public readonly int CompareTo(Indivisual other) => Math.Sign(other.Fitness - this.Fitness);

            /**
             *  format of file
             *  
             *  offset = 0: LABEL
             *  offset = LABEL_SIZE + 1: POPULATION_SIZE
             *  offset = LABEL_SIZE + 5: INDIVISUAL[0]
             *  ...
             **/
            public static Indivisual[] LoadPoolFromFile(string path)
            {
                Span<byte> buffer = stackalloc byte[LABEL_SIZE];
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                fs.Read(buffer);
                var label = Encoding.ASCII.GetString(buffer);
                var swapBytes = label == LABEL_INVERSED;

                if (!swapBytes && label != LABEL)
                    throw new InvalidDataException($"The format of \"{path}\" is invalid.");

                fs.Read(buffer[..sizeof(int)], swapBytes);
                return Enumerable.Range(0, BitConverter.ToInt32(buffer)).Select(_ => new Indivisual(fs, swapBytes)).ToArray();
            }

            public static void SavePoolAt(Indivisual[] pool, string path)
            {
                Span<byte> buffer = stackalloc byte[LABEL_SIZE];
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                Encoding.ASCII.GetBytes(LABEL).CopyTo(buffer);
                fs.Write(buffer);
                fs.Write(BitConverter.GetBytes(pool.Length));
                for (var i = 0; i < pool.Length; i++)
                    pool[i].WriteTo(fs);
            }
        }
    }
}
