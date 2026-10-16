using System;
using System.Globalization;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// Exact rational arithmetic, for the equation balancer.
    ///
    /// <para>Doubles cannot do this job. Gaussian elimination over a stoichiometry matrix produces
    /// denominators like 1/3 and 2/7 constantly, and a 1e-12 residue left by the division is
    /// indistinguishable from a genuine zero pivot - which is the difference between "this equation
    /// balances" and "it has no solution". An epsilon threshold picked to tell them apart is a
    /// guess that will be wrong for some equation, so there is no epsilon here at all.</para>
    ///
    /// <para>Always held in lowest terms with a positive denominator, so equality is structural and
    /// <c>Fraction.Of(2, 4) == Fraction.Of(1, 2)</c>.</para>
    ///
    /// <para>Overflow returns null rather than wrapping. A wrapped numerator would balance an
    /// equation with confident, silently wrong coefficients; the caller aborts instead.</para>
    /// </summary>
    public sealed class Fraction : IComparable<Fraction>, IEquatable<Fraction>
    {
        private Fraction(long num, long den)
        {
            Num = num;
            Den = den;
        }

        public long Num { get; }
        public long Den { get; }

        public static readonly Fraction Zero = new(0L, 1L);
        public static readonly Fraction One = new(1L, 1L);

        public bool IsZero => Num == 0L;
        public bool IsPositive => Num > 0L;
        public bool IsNegative => Num < 0L;

        /// <summary>
        /// A fraction in lowest terms.
        /// </summary>
        /// <returns>
        /// Null when <paramref name="d"/> is zero, or when reducing overflows - which only
        /// <see cref="long.MinValue"/> can do, since negating it has no positive counterpart.
        /// </returns>
        public static Fraction? Of(long n, long d = 1L)
        {
            if (d == 0L) return null;
            if (n == long.MinValue || d == long.MinValue) return null;
            if (n == 0L) return Zero;

            var sign = (n < 0) != (d < 0) ? -1L : 1L;
            var an = Math.Abs(n);
            var ad = Math.Abs(d);
            var g = Gcd(an, ad);
            return new Fraction(sign * (an / g), ad / g);
        }

        /// <summary>Never null: an int always reduces.</summary>
        public static Fraction OfInt(int n) => Of(n) ?? Zero;

        public static Fraction? operator +(Fraction a, Fraction b) =>
            Try.OrNull(() => Of(
                checked(checked(a.Num * b.Den) + checked(b.Num * a.Den)),
                checked(a.Den * b.Den)));

        public static Fraction? operator -(Fraction a, Fraction b) => a + (-b);

        public static Fraction? operator *(Fraction a, Fraction b) =>
            Try.OrNull(() => Of(checked(a.Num * b.Num), checked(a.Den * b.Den)));

        /// <returns>Null when <paramref name="b"/> is zero, or when the result overflows.</returns>
        public static Fraction? operator /(Fraction a, Fraction b)
        {
            if (b.IsZero) return null;
            return Try.OrNull(() => Of(checked(a.Num * b.Den), checked(a.Den * b.Num)));
        }

        public static Fraction operator -(Fraction f) => new(-f.Num, f.Den);

        public Fraction Abs() => Num < 0 ? -this : this;

        public int CompareTo(Fraction? other)
        {
            if (other is null) return 1;
            // Cross-multiplication is exact when it fits; fall back to doubles only when it
            // cannot, which costs precision but only for values far outside any real equation.
            return Try.OrDefault(
                () => checked(Num * other.Den).CompareTo(checked(other.Num * Den)),
                ToDouble().CompareTo(other.ToDouble()));
        }

        public double ToDouble() => (double)Num / Den;

        public bool Equals(Fraction? other) =>
            other is not null && Num == other.Num && Den == other.Den;

        public override bool Equals(object? obj) => Equals(obj as Fraction);

        public override int GetHashCode() => HashCode.Combine(Num, Den);

        public override string ToString() =>
            Den == 1L
                ? Num.ToString(CultureInfo.InvariantCulture)
                : Num.ToString(CultureInfo.InvariantCulture) + "/" + Den.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(Fraction? a, Fraction? b) =>
            a is null ? b is null : a.Equals(b);

        public static bool operator !=(Fraction? a, Fraction? b) => !(a == b);

        public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;
        public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;
        public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

        public static long Gcd(long a, long b)
        {
            while (b != 0L)
            {
                (a, b) = (b, a % b);
            }
            return Math.Abs(a);
        }

        /// <summary>Least common multiple, or null on overflow.</summary>
        public static long? Lcm(long a, long b)
        {
            if (a == 0L || b == 0L) return 0L;
            var g = Gcd(a, b);
            return Try.OrNullValue(() => checked(Math.Abs(a) / g * Math.Abs(b)));
        }
    }
}
