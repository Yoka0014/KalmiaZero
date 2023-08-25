using KalmiaZero.Reversi;
using KalmiaZero.NTuple;

namespace KalmiaZero_Test.NTuple
{
    public class PositionFeatures_Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Update_Test()
        {
            const int NUM_NTUPLES = 100;
            const int NTUPLE_SIZE = 7;

            var nTuples = (from _ in Enumerable.Range(0, NUM_NTUPLES) select new NTupleInfo(NTUPLE_SIZE)).ToArray();
            var expectedPf = new PositionFeatures(nTuples);
            var actualPf = new PositionFeatures(nTuples);   

            var pos = new Position();
            Span<Move> moves = stackalloc Move[Constants.MAX_NUM_MOVES];
            var numMoves = pos.GetNextMoves(ref moves);
            expectedPf.Init(ref pos, moves[..numMoves]);
            actualPf.Init(ref pos, moves[..numMoves]);

            var moveCount = 0;
            var passCount = 0;
            while(passCount < 2)
            {
                if(numMoves == 0)
                {
                    pos.Pass();
                    expectedPf.Pass();
                    actualPf.Pass();
                    passCount++;
                    continue;
                }

                var move = moves[Random.Shared.Next(numMoves)];
                pos.GenerateMove(ref move);
                pos.Update(ref move);
                numMoves = pos.GetNextMoves(ref moves);

                expectedPf.Init(ref pos, moves[..numMoves]);
                actualPf.Update(ref move, moves[..numMoves]);

                for(var i = 0; i < expectedPf.NumNTuples; i++)
                {
                    var expectedF = expectedPf.GetFeatures(i);
                    var actualF = actualPf.GetFeatures(i);  
                    for(var j = 0; j < expectedF.Length; j++)
                        Assert.AreEqual(expectedF[j], actualF[j]);
                }
                moveCount++;
            }
            
        }
    }
}