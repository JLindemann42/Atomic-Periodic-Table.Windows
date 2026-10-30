using System;

namespace Atomic.Core.Text
{
    /// <summary>
    /// Stands in for Kotlin's <c>runCatching { … }.getOrNull()</c>.
    ///
    /// <para>One helper rather than empty catch blocks scattered through the port: an empty catch
    /// is indistinguishable from a forgotten one, and the Kotlin sources use this idiom in places
    /// where swallowing really is correct - overflow in exact arithmetic, a malformed authored
    /// value - and nowhere else.</para>
    ///
    /// <para>Only arithmetic and parse failures are caught. Anything else is a bug and should
    /// surface.</para>
    /// </summary>
    public static class Try
    {
        public static T? OrNull<T>(Func<T> body) where T : class
        {
            try
            {
                return body();
            }
            catch (Exception e) when (IsExpected(e))
            {
                return null;
            }
        }

        public static T? OrNullValue<T>(Func<T> body) where T : struct
        {
            try
            {
                return body();
            }
            catch (Exception e) when (IsExpected(e))
            {
                return null;
            }
        }

        public static T OrDefault<T>(Func<T> body, T fallback)
        {
            try
            {
                return body();
            }
            catch (Exception e) when (IsExpected(e))
            {
                return fallback;
            }
        }

        private static bool IsExpected(Exception e) =>
            e is OverflowException or FormatException or ArgumentException
                or InvalidCastException or IndexOutOfRangeException
                or ArgumentOutOfRangeException or DivideByZeroException;
    }
}
