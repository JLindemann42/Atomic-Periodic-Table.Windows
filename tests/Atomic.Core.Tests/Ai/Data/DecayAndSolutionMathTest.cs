using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>Ported from <c>DecayMathTest.kt</c>.</summary>
    public class DecayMathTest
    {
        private const double Year = 3.15576e7;
        private const double CarbonFourteen = 5730 * Year;

        [Fact]
        public void OneHalfLifeLeavesHalf()
        {
            Assert.Equal(1.0, DecayMath.HalfLives(CarbonFourteen, CarbonFourteen)!.Value, 1e-9);
            Assert.Equal(0.5, DecayMath.FractionRemaining(1.0), 1e-12);
            Assert.Equal(50.0, DecayMath.Remaining(100.0, CarbonFourteen, CarbonFourteen)!.Value, 1e-9);
        }

        [Fact]
        public void ThreeHalfLivesLeaveAnEighth() =>
            Assert.Equal(12.5, DecayMath.Remaining(100.0, 3 * CarbonFourteen, CarbonFourteen)!.Value, 1e-9);

        [Fact]
        public void ElapsedIsTheInverseOfRemaining()
        {
            var elapsed = DecayMath.ElapsedFor(100.0, 25.0, CarbonFourteen)!.Value;
            Assert.Equal(2 * CarbonFourteen, elapsed, CarbonFourteen * 1e-9);
            Assert.Equal(25.0, DecayMath.Remaining(100.0, elapsed, CarbonFourteen)!.Value, 1e-6);
        }

        [Fact]
        public void ASampleCannotGrow()
        {
            // The logarithm would happily supply a negative time for this.
            Assert.Null(DecayMath.ElapsedFor(25.0, 100.0, CarbonFourteen));
            Assert.Null(DecayMath.ElapsedFor(0.0, 10.0, CarbonFourteen));
            Assert.Null(DecayMath.ElapsedFor(100.0, 0.0, CarbonFourteen));
        }

        [Fact]
        public void NonPositiveOrInfiniteHalfLivesHaveNoAnswer()
        {
            Assert.Null(DecayMath.HalfLives(1.0, 0.0));
            Assert.Null(DecayMath.HalfLives(1.0, -1.0));
            Assert.Null(DecayMath.HalfLives(1.0, double.PositiveInfinity));
            Assert.Null(DecayMath.HalfLives(-1.0, 1.0));
            Assert.Null(DecayMath.DecayConstant(0.0));
        }

        [Fact]
        public void DecayConstantIsLnTwoOverHalfLife()
        {
            var lambda = DecayMath.DecayConstant(CarbonFourteen)!.Value;
            Assert.Equal(System.Math.Log(2.0) / CarbonFourteen, lambda, 1e-18);
        }

        [Fact]
        public void PastSixtyHalfLivesThereIsEffectivelyNothing()
        {
            // Printing 3.2e-25 g would imply a measurement nobody could make.
            var left = DecayMath.FractionRemaining(DecayMath.EffectivelyGone);
            Assert.True(left < 1e-18, $"expected under 1e-18, got {left}");
        }
    }

    /// <summary>Ported from <c>SolutionMathTest.kt</c>.</summary>
    public class SolutionMathTest
    {
        [Fact]
        public void AnyTwoOfTheThreeCompleteTheThird()
        {
            var byVolume = SolutionMath.Complete(null, 0.5, 2.0)!.Value;
            Assert.Equal(1.0, byVolume.Moles, 1e-9);

            var byMoles = SolutionMath.Complete(1.0, 0.5, null)!.Value;
            Assert.Equal(2.0, byMoles.Litres, 1e-9);

            var byMolarity = SolutionMath.Complete(1.0, null, 2.0)!.Value;
            Assert.Equal(0.5, byMolarity.Molarity, 1e-9);
        }

        [Fact]
        public void FewerThanTwoKnownsHasNoAnswer()
        {
            Assert.Null(SolutionMath.Complete(1.0, null, null));
            Assert.Null(SolutionMath.Complete(null, null, null));
        }

        [Fact]
        public void ZeroAndNegativeInputsAreAMisreadQueryNotASolution()
        {
            // A zero volume or a negative concentration is a parse that went wrong upstream.
            Assert.Null(SolutionMath.Complete(1.0, 0.0, null));
            Assert.Null(SolutionMath.Complete(-1.0, 0.5, null));
        }

        [Fact]
        public void MassAndMolesRoundTrip()
        {
            Assert.Equal(98.0, SolutionMath.MassOf(1.0, 98.0)!.Value, 1e-9);
            Assert.Equal(0.5, SolutionMath.MolesOf(49.0, 98.0)!.Value, 1e-9);
            Assert.Null(SolutionMath.MassOf(1.0, 0.0));
        }

        [Fact]
        public void DilutionSolvesForWhicheverIsMissing()
        {
            // 100 mL of 1 M diluted to 500 mL gives 0.2 M.
            var forC2 = SolutionMath.DilutionOf(1.0, 0.1, null, 0.5)!;
            Assert.Equal(0.2, forC2.C2, 1e-9);

            var forV2 = SolutionMath.DilutionOf(1.0, 0.1, 0.2, null)!;
            Assert.Equal(0.5, forV2.V2, 1e-9);

            var forC1 = SolutionMath.DilutionOf(null, 0.1, 0.2, 0.5)!;
            Assert.Equal(1.0, forC1.C1, 1e-9);

            var forV1 = SolutionMath.DilutionOf(1.0, null, 0.2, 0.5)!;
            Assert.Equal(0.1, forV1.V1, 1e-9);
        }

        [Fact]
        public void ExactlyThreeKnownsIsTheWholeCondition()
        {
            // With four the user has asked nothing; with two the equation has a family of answers.
            Assert.Null(SolutionMath.DilutionOf(1.0, 0.1, 0.2, 0.5));
            Assert.Null(SolutionMath.DilutionOf(1.0, 0.1, null, null));
        }

        [Fact]
        public void SolventToAddIsNegativeWhenTheTargetIsStronger()
        {
            // A real answer: it says evaporate or make up differently, not dilute.
            Assert.Equal(0.4, SolutionMath.SolventToAdd(0.1, 0.5), 1e-9);
            Assert.Equal(-0.4, SolutionMath.SolventToAdd(0.5, 0.1), 1e-9);
        }
    }
}
