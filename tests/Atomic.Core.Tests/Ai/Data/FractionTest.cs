using Atomic.Core.Ai.Data;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>Ported from <c>FractionTest.kt</c>.</summary>
    public class FractionTest
    {
        [Fact]
        public void FractionsAreHeldInLowestTermsSoEqualityIsStructural()
        {
            Assert.Equal(Fraction.Of(1, 2), Fraction.Of(2, 4));
            Assert.Equal(Fraction.Of(1, 2), Fraction.Of(50, 100));
            Assert.Equal(Fraction.Of(1, 2)!.GetHashCode(), Fraction.Of(3, 6)!.GetHashCode());
        }

        [Fact]
        public void TheSignLivesOnTheNumeratorSoTwoSpellingsOfMinusHalfAgree()
        {
            var a = Fraction.Of(-1, 2)!;
            var b = Fraction.Of(1, -2)!;
            Assert.Equal(a, b);
            Assert.Equal(-1L, a.Num);
            Assert.Equal(2L, a.Den);
            Assert.True(a.IsNegative);
        }

        /// <summary>The whole reason this type exists: a double leaves 0.49999999999999994 here.</summary>
        [Fact]
        public void AdditionIsExact()
        {
            var third = Fraction.Of(1, 3)!;
            var sixth = Fraction.Of(1, 6)!;
            Assert.Equal(Fraction.Of(1, 2), third + sixth);
            Assert.Equal(Fraction.Zero, third - third);
        }

        [Fact]
        public void MultiplicationAndDivisionReduceAsTheyGo()
        {
            Assert.Equal(Fraction.Of(1, 6), Fraction.Of(1, 2)! * Fraction.Of(1, 3)!);
            Assert.Equal(Fraction.Of(3, 2), Fraction.Of(1, 2)! / Fraction.Of(1, 3)!);
            Assert.Null(Fraction.Of(1, 2)! / Fraction.Zero);
        }

        [Fact]
        public void AZeroDenominatorIsNotAFraction() => Assert.Null(Fraction.Of(1, 0));

        [Fact]
        public void ComparisonOrdersByValueNotByNumerator()
        {
            Assert.True(Fraction.Of(1, 3)! < Fraction.Of(1, 2)!);
            Assert.True(Fraction.Of(-5, 1)! < Fraction.Zero);
            Assert.Equal(0, Fraction.Of(2, 4)!.CompareTo(Fraction.Of(1, 2)!));
        }

        [Fact]
        public void GcdAndLcmAgreeWithTheirDefinitions()
        {
            Assert.Equal(6L, Fraction.Gcd(12, 18));
            Assert.Equal(7L, Fraction.Gcd(7, 0));
            Assert.Equal(36L, Fraction.Lcm(12, 18));
            Assert.Equal(0L, Fraction.Lcm(0, 5));
        }

        /// <summary>
        /// Overflow must surface as null. A wrapped numerator would balance an equation with
        /// confident, silently wrong coefficients - the one outcome the balancer must never
        /// produce.
        /// </summary>
        [Fact]
        public void OverflowReturnsNullRatherThanWrapping()
        {
            var big = Fraction.Of(long.MaxValue / 2, 1)!;
            Assert.Null((big + big)! + big);
            Assert.Null(big * big);
            // Consecutive integers are coprime, so the lcm is their product and cannot fit.
            Assert.Null(Fraction.Lcm(long.MaxValue, long.MaxValue - 1));
            Assert.Null(Fraction.Of(long.MinValue, 1));
        }

        [Fact]
        public void ZeroAndOneBehave()
        {
            Assert.True(Fraction.Zero.IsZero);
            Assert.False(Fraction.One.IsZero);
            Assert.True(Fraction.One.IsPositive);
            Assert.Equal(Fraction.One, Fraction.Of(5, 5));
            Assert.Equal(Fraction.Zero, Fraction.Of(0, 7));
            Assert.Equal("1/2", Fraction.Of(1, 2)!.ToString());
            Assert.Equal("3", Fraction.Of(6, 2)!.ToString());
        }

        [Fact]
        public void ToStringIsCultureInvariant()
        {
            // Under a comma-decimal culture a naive long.ToString() is unaffected, but the habit
            // matters: this type feeds the balancer's rendered coefficients.
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("1/2", Fraction.Of(1, 2)!.ToString());
                Assert.Equal("1234567", Fraction.Of(1234567, 1)!.ToString());
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }
    }
}
