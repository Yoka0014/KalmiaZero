//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;

//using KalmiaZero.Evaluation;
//using KalmiaZero.NTuple;
//using KalmiaZero.Reversi;
//using KalmiaZero.Search;
//using KalmiaZero.Search.AlphaBeta;

//namespace KalmiaZero.Learn
//{
//    public record class TDLeafTrainerConfig
//    {
//        public int NumEpisodes { get; init; } = 250_000;
//        public int NumInitialRandomMoves { get; init; } = 1;
//        public MiniMaxType LearningRate { get; init; } = (MiniMaxType)0.2;
//        public MiniMaxType DiscountRate { get; init; } = 1;
//        public double InitialExplorationRate { get; init; } = 0.1;
//        public double FinalExplorationRate { get; init; } = 0.1;
//        public MiniMaxType EligibilityTraceFactor { get; init; } = (MiniMaxType)0.7;
//        public MiniMaxType HorizonCutFactor { get; init; } = (MiniMaxType)0.1;
//        public MiniMaxType TCLFactor { get; init; } = (MiniMaxType)2.7;
//        public int SearchDepth { get; init; } = 3;
//        public long TTSize { get; init; } = 512L * 1024 * 1024;

//        public string WorkDir { get; init; } = Environment.CurrentDirectory;
//        public string WeightsFileName { get; init; } = "value_func_weights_tdl";
//        public int SaveWeightsInterval { get; init; } = 10000;
//        public bool SaveOnlyLatestWeights { get; init; } = false;
//    }

//    public class TDLeafTrainer
//    {
//        const double TCL_EPSILON = 1.0e-4;

//        public string Label { get; }
//        readonly TDLeafTrainerConfig CONFIG;
//        readonly double EXPLORATION_RATE_DELTA;
//        readonly string WEIGHTS_FILE_PATH;
//        readonly StreamWriter logger;

//        readonly Searcher searcher;
//        readonly ValueFunction<MiniMaxType> valueFunc;
//        readonly PastStatesBuffer pastStatesBuffer;
//        readonly MiniMaxType[] weightDeltaSum;
//        readonly MiniMaxType[] weightDeltaAbsSum;
//        MiniMaxType biasDeltaSum;
//        MiniMaxType biasDeltaAbsSum;
//        MiniMaxType meanWeightDelta;

//        readonly Random rand;

//        public TDLeafTrainer(ValueFunction<MiniMaxType> valueFunc, TDLeafTrainerConfig config, int randSeed = -1)
//        : this(valueFunc, config, Console.OpenStandardOutput(), randSeed) { }

//        public TDLeafTrainer(ValueFunction<MiniMaxType> valueFunc, TDLeafTrainerConfig config, Stream logStream, int randSeed = -1)
//        : this(string.Empty, valueFunc, config, logStream, randSeed) { }

//        public TDLeafTrainer(string label, ValueFunction<MiniMaxType> valueFunc, TDLeafTrainerConfig config)
//        : this(label, valueFunc, config, Console.OpenStandardOutput()) { }

//        public TDLeafTrainer(string label, ValueFunction<MiniMaxType> valueFunc, TDLeafTrainerConfig config, Stream logStream, int randSeed = -1)
//        {
//            this.Label = label;
//            this.CONFIG = config;
//            this.EXPLORATION_RATE_DELTA = (config.InitialExplorationRate - config.FinalExplorationRate) / config.NumEpisodes;
//            this.WEIGHTS_FILE_PATH = Path.Combine(this.CONFIG.WorkDir, $"{config.WeightsFileName}_{"{0}"}.bin");

//            this.valueFunc = valueFunc;
//            var numWeights = valueFunc.Weights.Length;
//            this.weightDeltaSum = new MiniMaxType[numWeights];
//            this.weightDeltaAbsSum = new MiniMaxType[numWeights];
//            int capasity;
//            if (config.EligibilityTraceFactor != 0)
//                capasity = int.CreateChecked(MiniMaxType.Log(config.HorizonCutFactor, config.EligibilityTraceFactor)) + 1;
//            else
//                capasity = 1;
//            this.pastStatesBuffer = new PastStatesBuffer(capasity, valueFunc.NTuples);

//            this.searcher = new Searcher(this.valueFunc, this.CONFIG.TTSize);
//            this.searcher.PVNotificationIntervalMs = int.MaxValue;

//            this.rand = (randSeed >= 0) ? new Random(randSeed) : new Random(Random.Shared.Next());

//            this.logger = new StreamWriter(logStream);
//            this.logger.AutoFlush = false;
//        }

//        public void Train()
//        {
//            var explorationRate = this.CONFIG.InitialExplorationRate;
//            var tclEpsilon = (MiniMaxType)TCL_EPSILON;
//            Array.Fill(this.weightDeltaSum, tclEpsilon);
//            Array.Fill(this.weightDeltaAbsSum, tclEpsilon);
//            this.biasDeltaSum = this.biasDeltaAbsSum = tclEpsilon;

//            WriteLabel();
//            this.logger.WriteLine("Start learning.\n");
//            WriteParams(explorationRate);
//            this.logger.Flush();

//            for (var episodeID = 0; episodeID < this.CONFIG.NumEpisodes; episodeID++)
//            {
//                RunEpisode(explorationRate);
//                explorationRate -= EXPLORATION_RATE_DELTA;

//                if ((episodeID + 1) % this.CONFIG.SaveWeightsInterval == 0)
//                {
//                    WriteLabel();

//                    var fromEpisodeID = episodeID - this.CONFIG.SaveWeightsInterval + 1;
//                    this.logger.WriteLine($"Episodes {fromEpisodeID} to {episodeID} have done.");

//                    var path = string.Format(this.WEIGHTS_FILE_PATH, episodeID);
//                    this.valueFunc.SaveToFile(path);

//                    this.logger.WriteLine($"Weights were saved at \"{path}\"\n");
//                    WriteParams(explorationRate);
//                    this.logger.WriteLine();
//                    this.logger.Flush();
//                }
//            }
//        }

//        void RunEpisode(double explorationRate)
//        {
//            var game = new GameInfo(valueFunc.NTuples);
//            var leafGame = new GameInfo(valueFunc.NTuples);
//            var nextLeafGame = new GameInfo(valueFunc.NTuples);
//            this.searcher.SetRootGame(ref game);
//            this.pastStatesBuffer.Clear();
//            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];

//            var moveCount = 0;
//            while (true)
//            {
//                game.CopyTo(ref leafGame);
//                game.CopyTo(ref nextLeafGame);

//                if(game.Moves.Length == 0)  // pass
//                {
//                    game.Pass();
//                    this.searcher.PassRootGame();

//                    // terminal state
//                    if (game.Moves.Length == 0) 
//                        break;

//                    continue;
//                }

//                var searchRes = this.searcher.Search(this.CONFIG.SearchDepth);
//                MoveToLeaf(ref leafGame, searchRes.PV);
//                this.pastStatesBuffer.Add(leafGame.FeatureVector);

//                // ここで葉ノードの価値を価値関数で計算.
//                // Searcherから受け取ったMiniMax値をそのまま用いない理由は以下．
//                // 1. 葉ノードで終局した場合，価値関数の値ではなく報酬そのものがSearcherから返ってくるから.
//                // 2. Searcherでは探索の都合上，桁落ちが発生しているから(実際ほとんど学習に影響はないと思うが)． see also: Searcher.EvaluateNode
//                var v = this.valueFunc.Predict(leafGame.FeatureVector);

//                MiniMaxType nextV;
//                if (moveCount < this.CONFIG.NumInitialRandomMoves || Random.Shared.NextDouble() < explorationRate)   // random move
//                {
//                    Move move = game.Moves[this.rand.Next(game.Moves.Length)];
//                    game.Position.GenerateMove(ref move);
//                    game.Update(ref move);
//                    this.searcher.UpdateRootGame(ref move);
//                    nextLeafGame.Update(ref move);

//                    if (game.Moves.Length == 0)
//                    {
//                        game.Pass();
//                        this.searcher.PassRootGame();
//                        if (game.Moves.Length == 0) // terminal
//                        {
//                            var reward = GetReward(game.Position.DiscDiff);
//                            if (game.Position.SideToMove != leafGame.Position.SideToMove)
//                                reward = 1 - reward;
//                            Fit(leafGame.Position.SideToMove, reward - v);
//                            break;
//                        }
//                    }

//                    var nextSearchRes = this.searcher.Search(this.CONFIG.SearchDepth);
//                    MoveToLeaf(ref nextLeafGame, nextSearchRes.PV);

//                    if (nextLeafGame.Position.IsGameOver)
//                        nextV = GetReward(nextLeafGame.Position.DiscDiff);
//                    else
//                        nextV = this.valueFunc.Predict(nextLeafGame.FeatureVector);

//                    if (nextLeafGame.Position.SideToMove != leafGame.Position.SideToMove)
//                        nextV = 1 - nextV;
//                }
//                else   // greedy
//                {
//                    var move = new Move(searchRes.BestMove);
//                    game.Position.GenerateMove(ref move);
//                    game.Update(ref move);
//                    this.searcher.UpdateRootGame(ref move);
//                    nextLeafGame.Update(ref move);

//                    if (game.Moves.Length == 0)
//                    {
//                        game.Pass();
//                        this.searcher.PassRootGame();
//                        if (game.Moves.Length == 0) // terminal
//                        {
//                            var reward = GetReward(game.Position.DiscDiff);
//                            if (game.Position.SideToMove != leafGame.Position.SideToMove)
//                                reward = 1 - reward;
//                            Fit(leafGame.Position.SideToMove, reward - v);
//                            break;
//                        }
//                    }

//                    var nextSearchRes = this.searcher.Search(this.CONFIG.SearchDepth);
//                    MoveToLeaf(ref nextLeafGame, nextSearchRes.PV);

//                    if (nextLeafGame.Position.IsGameOver)
//                        nextV = GetReward(nextLeafGame.Position.DiscDiff);
//                    else
//                        nextV = this.valueFunc.Predict(nextLeafGame.FeatureVector);

//                    if (nextLeafGame.Position.SideToMove != leafGame.Position.SideToMove)
//                        nextV = 1 - nextV;
//                }

//                Fit(leafGame.Position.SideToMove, this.CONFIG.DiscountRate * nextV - v);
//                moveCount++;
//            }
//        }

//        void WriteLabel()
//        {
//            if (!string.IsNullOrEmpty(this.Label))
//                this.logger.WriteLine($"[{this.Label}]");
//        }

//        void WriteParams(double explorationRate)
//        {
//            this.logger.WriteLine($"Lambda: {this.CONFIG.EligibilityTraceFactor}");
//            this.logger.WriteLine($"ExplorationRate: {explorationRate}");
//            this.logger.WriteLine($"MeanLearningRate: {CalcMeanLearningRate()}");
//        }

//        MiniMaxType CalcMeanLearningRate()
//        {
//            var sum = (MiniMaxType)0;
//            foreach ((var n, var a) in this.weightDeltaSum.Zip(this.weightDeltaAbsSum))
//                sum += Decay(MiniMaxType.Abs(n / a));
//            sum += Decay(this.biasDeltaSum / this.biasDeltaAbsSum);
//            return this.CONFIG.LearningRate * sum / (this.weightDeltaSum.Length + 1);
//        }

//        unsafe void Fit(DiscColor color, MiniMaxType tdError)
//        {
//            var alpha = this.CONFIG.LearningRate;
//            var beta = this.CONFIG.TCLFactor;
//            MiniMaxType eligibility = 1;

//            fixed (MiniMaxType* weights = this.valueFunc.Weights)
//            fixed (MiniMaxType* weightDeltaSum = this.weightDeltaSum)
//            fixed (MiniMaxType* weightDeltaAbsSum = this.weightDeltaAbsSum)
//            {
//                foreach (var posFeatureVec in this.pastStatesBuffer)
//                {
//                    var delta = (posFeatureVec.SideToMove == color)
//                        ? eligibility * tdError
//                        : eligibility * (this.CONFIG.DiscountRate - 1 - tdError);   // inverse tdError: DiscountRate * (1.0 - nextV) - (1.0 - v)

//                    if (posFeatureVec.SideToMove == DiscColor.Black)
//                        ApplyGradients<Black>(posFeatureVec, weights, weightDeltaSum, weightDeltaAbsSum, alpha, beta, delta);
//                    else
//                        ApplyGradients<White>(posFeatureVec, weights, weightDeltaSum, weightDeltaAbsSum, alpha, beta, delta);

//                    var reg = (MiniMaxType)1 / (posFeatureVec.NumNTuples + 1);
//                    var lr = reg * alpha * Decay(MiniMaxType.Abs(this.biasDeltaSum) / this.biasDeltaAbsSum);
//                    var db = lr * delta;
//                    this.valueFunc.Bias += db;
//                    this.biasDeltaSum += db;
//                    this.biasDeltaAbsSum += MiniMaxType.Abs(db);

//                    eligibility *= this.CONFIG.DiscountRate * this.CONFIG.EligibilityTraceFactor;
//                }
//            }
//        }

//        unsafe void ApplyGradients<DiscColor>(PositionFeatureVector posFeatureVec, MiniMaxType* weights,
//            MiniMaxType* weightDeltaSum, MiniMaxType* weightDeltaAbsSum,
//            MiniMaxType alpha, MiniMaxType beta, MiniMaxType delta) where DiscColor : IDiscColor
//        {
//            for (var i = 0; i < posFeatureVec.Features.Length; i++)
//            {
//                var offset = this.valueFunc.NTupleOffset[i];
//                var w = weights + offset;
//                var ww = weights + this.valueFunc.DiscColorOffset[(int)Reversi.DiscColor.White] + offset;
//                var dwSum = weightDeltaSum + offset;
//                var dwAbsSum = weightDeltaAbsSum + offset;

//                ref Feature feature = ref posFeatureVec.Features[i];
//                fixed (FeatureType* opp = posFeatureVec.NTuples.GetRawOpponentFeatureTable(i))
//                fixed (FeatureType* mirror = posFeatureVec.NTuples.GetRawMirroredFeatureTable(i))
//                {
//                    var reg = (MiniMaxType)1 / ((posFeatureVec.NumNTuples + 1) * feature.Length);
//                    for (var j = 0; j < feature.Length; j++)
//                    {
//                        var f = (typeof(DiscColor) == typeof(Black)) ? feature[j] : opp[feature[j]];
//                        var mf = mirror[f];

//                        var lr = reg * alpha * Decay(MiniMaxType.Abs(dwSum[i]) / dwAbsSum[i]);
//                        var dw = lr * delta;
//                        var absDW = MiniMaxType.Abs(dw);

//                        if (mf != f)
//                        {
//                            dw *= (MiniMaxType)0.5;
//                            absDW *= (MiniMaxType)0.5;
//                            w[mf] += dw;
//                            ww[opp[mf]] = w[mf];
//                            dwSum[mf] += dw;
//                            dwAbsSum[mf] += absDW;
//                        }

//                        w[f] += dw;
//                        ww[opp[f]] = w[f];
//                        dwSum[f] += dw;
//                        dwAbsSum[f] += absDW;
//                    }
//                }
//            }
//        }

//        MiniMaxType Decay(MiniMaxType x) => MiniMaxType.Exp(this.CONFIG.TCLFactor * (x - 1));

//        static void MoveToLeaf(ref GameInfo game, IEnumerable<BoardCoordinate> pv)
//        {
//            Move move = new();
//            foreach (var moveCoord in pv)
//            {
//                if (game.Moves.Length == 0)
//                    game.Pass();

//                move.Coord = moveCoord;
//                game.Position.GenerateMove(ref move);
//                game.Update(ref move);
//            }
//        }

//        static MiniMaxType GetReward(int discDiff)
//        {
//            if (discDiff == 0)
//                return (MiniMaxType)0.5;
//            else
//                return (discDiff > 0) ? 1 : 0;
//        }

//        class PastStatesBuffer : IEnumerable<PositionFeatureVector>
//        {
//            public int Capasity => this.posFeatureVecs.Length;
//            public int Count { get; private set; } = 0;

//            readonly PositionFeatureVector[] posFeatureVecs;
//            int loc = 0;

//            public PastStatesBuffer(int capasity, NTupleGroup nTuples)
//            {
//                this.posFeatureVecs = Enumerable.Range(0, capasity).Select(_ => new PositionFeatureVector(nTuples)).ToArray();
//            }

//            public void Clear() => this.loc = 0;

//            public void Add(PositionFeatureVector pfVec)
//            {
//                pfVec.CopyTo(this.posFeatureVecs[this.loc]);
//                this.loc = (this.loc + 1) % this.Capasity;
//                this.Count = Math.Min(this.Count + 1, this.Capasity);
//            }

//            public IEnumerator<PositionFeatureVector> GetEnumerator() => new Enumerator(this);

//            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

//            public class Enumerator : IEnumerator<PositionFeatureVector>
//            {
//                public PositionFeatureVector Current { get; private set; }

//                object IEnumerator.Current => this.Current;

//                readonly PastStatesBuffer pastStatesBuffer;
//                int idx;
//                int moveCount;

//                public Enumerator(PastStatesBuffer pastStatesBuffer)
//                {
//                    this.pastStatesBuffer = pastStatesBuffer;
//                    Reset();
//                    Debug.Assert(this.Current is not null);
//                }

//                public void Dispose() { }

//                public bool MoveNext()
//                {
//                    if (this.moveCount == this.pastStatesBuffer.Count)
//                        return false;

//                    var nextIdx = this.idx - 1;
//                    if (nextIdx < 0)
//                        nextIdx = this.pastStatesBuffer.Count - 1;
//                    this.Current = this.pastStatesBuffer.posFeatureVecs[this.idx];
//                    this.idx = nextIdx;
//                    this.moveCount++;
//                    return true;
//                }

//                public void Reset()
//                {
//                    this.Current = PositionFeatureVector.Empty;
//                    this.idx = this.pastStatesBuffer.loc - 1;
//                    if (idx < 0)
//                        this.idx = this.pastStatesBuffer.Count - 1;
//                    this.moveCount = 0;
//                }
//            }
//        }
//    }
//}
