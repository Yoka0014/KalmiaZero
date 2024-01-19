using KalmiaZero.Evaluation;
using KalmiaZero.NTuple;
using KalmiaZero.Reversi;

namespace KalmiaZero_Test.Evaluation
{
    public class ValueFunction_Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void LoadFromFileAndSaveToFile_Test()
        {
            const double DELTA = 1.0e-6f;
            const int NUM_NTUPLES = 100;
            const int NTUPLE_SIZE = 7;
            const int NUM_SAMPLES = 10;

            for (var numMovesPerPhase = 1; numMovesPerPhase <= 60; numMovesPerPhase++)
            {
                var nTuples = (from _ in Enumerable.Range(0, NUM_NTUPLES) select new NTupleInfo(NTUPLE_SIZE)).ToArray();
                var valueFunc = new ValueFunction<double>(new NTupleGroup(nTuples), numMovesPerPhase);
                valueFunc.InitWeightsWithUniformRand(0.0f, 0.001f);

                var fileName = Path.GetRandomFileName();
                valueFunc.SaveToFile(fileName);
                var loaded = ValueFunction<float>.LoadFromFile(fileName);

                for (var nTupleID = 0; nTupleID < nTuples.Length; nTupleID++)
                {
                    var expectedTuple = valueFunc.NTuples.Tuples[nTupleID];
                    var actualTuple = valueFunc.NTuples.Tuples[nTupleID];
                    Assert.IsTrue(expectedTuple.GetCoordinates(0).SequenceEqual(actualTuple.GetCoordinates(0)));

                    var expectedW = valueFunc.GetWeights(DiscColor.Black, nTupleID);
                    var actualW = loaded.GetWeights(DiscColor.Black, nTupleID);
                    Assert.AreEqual(expectedW.Length, actualW.Length);
                    for (var i = 0; i < expectedW.Length; i++)
                        Assert.AreEqual(expectedW[i], actualW[i], DELTA);
                }

                Assert.AreEqual(valueFunc.Bias.Length, loaded.Bias.Length);
                for (var phase = 0; phase < valueFunc.NumPhases; phase++)
                    Assert.AreEqual(valueFunc.Bias[phase], loaded.Bias[phase], DELTA);

                File.Delete(fileName);
            }
        }

        [Test]
        public void Predict_Test()
        {
            const int NUM_NTUPLES = 100;
            const int NTUPLE_SIZE = 7;
            const float DELTA = 1.0e-6f;

            for (var numMovesPerPhase = 1; numMovesPerPhase <= 60; numMovesPerPhase++)
            {
                var tuples = (from _ in Enumerable.Range(0, NUM_NTUPLES) select new NTupleInfo(NTUPLE_SIZE)).ToArray();
                var nTuples = new NTupleGroup(tuples);
                var valueFunc = new ValueFunction<float>(nTuples, numMovesPerPhase);
                valueFunc.InitWeightsWithUniformRand(0.0f, 0.001f);

                var pos = new Position();
                var pfv = new PositionFeatureVector(nTuples);
                Span<Move> moves = stackalloc Move[Constants.NUM_SQUARES];

                var numMoves = pos.GetNextMoves(ref moves);
                pfv.Init(ref pos, moves[..numMoves]);

                var history = new List<BoardCoordinate>();
                var passCount = 0;
                while (passCount < 2)
                {
                    if (numMoves == 0)
                    {
                        pos.Pass();
                        numMoves = pos.GetNextMoves(ref moves);
                        pfv.Pass(moves[..numMoves]);
                        numMoves = pos.GetNextMoves(ref moves);
                        history.Add(BoardCoordinate.Pass);
                        passCount++;
                        continue;
                    }

                    passCount = 0;
                    var expected = valueFunc.PredictLogit(pfv);
                    for (var i = 0; i < 3; i++)
                    {
                        pos.Rotate90Clockwise();
                        for (var j = 0; j < numMoves; j++)
                            moves[j].Coord = Utils.TO_ROTATE90_COORD[(int)moves[j].Coord];

                        pfv.Init(ref pos, moves[..numMoves]);
                        Assert.AreEqual(expected, valueFunc.PredictLogit(pfv), DELTA);
                    }

                    pos.Rotate90Clockwise();
                    for (var j = 0; j < numMoves; j++)
                        moves[j].Coord = Utils.TO_ROTATE90_COORD[(int)moves[j].Coord];

                    pos.MirrorHorizontal();
                    for (var j = 0; j < numMoves; j++)
                        moves[j].Coord = Utils.TO_HORIZONTAL_MIRROR_COORD[(int)moves[j].Coord];

                    for (var i = 0; i < 4; i++)
                    {
                        pfv.Init(ref pos, moves[..numMoves]);
                        Assert.AreEqual(expected, valueFunc.PredictLogit(pfv), DELTA);
                        pos.Rotate90Clockwise();
                        for (var j = 0; j < numMoves; j++)
                            moves[j].Coord = Utils.TO_ROTATE90_COORD[(int)moves[j].Coord];
                    }

                    pos.MirrorHorizontal();
                    for (var j = 0; j < numMoves; j++)
                        moves[j].Coord = Utils.TO_HORIZONTAL_MIRROR_COORD[(int)moves[j].Coord];
                    pfv.Init(ref pos, moves[..numMoves]);

                    var move = moves[Random.Shared.Next(numMoves)];
                    history.Add(move.Coord);
                    pos.GenerateMove(ref move);
                    pos.Update(ref move);
                    numMoves = pos.GetNextMoves(ref moves);
                    pfv.Update(ref move, moves[..numMoves]);
                }
            }
        }

        [Test]
        public void PredictWithBlackWeights_Test()
        {
            const int NUM_NTUPLES = 100;
            const int NTUPLE_SIZE = 7;
            const float DELTA = 1.0e-6f;

            for (var numMovesPerPhase = 1; numMovesPerPhase <= 60; numMovesPerPhase++)
            {
                var tuples = (from _ in Enumerable.Range(0, NUM_NTUPLES) select new NTupleInfo(NTUPLE_SIZE)).ToArray();
                var nTuples = new NTupleGroup(tuples);
                var valueFunc = new ValueFunction<float>(nTuples, numMovesPerPhase);
                valueFunc.InitWeightsWithUniformRand(0.0f, 0.001f);

                var pos = new Position();
                var pfv = new PositionFeatureVector(nTuples);
                Span<Move> moves = stackalloc Move[Constants.NUM_SQUARES];

                var numMoves = pos.GetNextMoves(ref moves);
                pfv.Init(ref pos, moves[..numMoves]);

                var history = new List<BoardCoordinate>();
                var passCount = 0;
                while (passCount < 2)
                {
                    if (numMoves == 0)
                    {
                        pos.Pass();
                        numMoves = pos.GetNextMoves(ref moves);
                        pfv.Pass(moves[..numMoves]);
                        numMoves = pos.GetNextMoves(ref moves);
                        history.Add(BoardCoordinate.Pass);
                        passCount++;
                        continue;
                    }

                    passCount = 0;
                    Assert.AreEqual(valueFunc.PredictLogit(pfv), valueFunc.PredictLogitWithBlackWeights(pfv), DELTA);

                    var move = moves[Random.Shared.Next(numMoves)];
                    history.Add(move.Coord);
                    pos.GenerateMove(ref move);
                    pos.Update(ref move);
                    numMoves = pos.GetNextMoves(ref moves);
                    pfv.Update(ref move, moves[..numMoves]);
                }
            }
        }
    }
}