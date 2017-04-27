BigRational is implemented in the single BigRational structure.  You can find the details for the functionality of the class below.

[Class Definition](#ClassDefinition)
[Public Properties](#PublicProperties)
[Public Instance Methods](#PublicInstanceMethods)
[Constructors](#Constructors)
[Public Static Methods](#PublicStaticMethods)
[Operator Overloads](#OperatorOverloads)
[Explicit Conversions](#ExplicitConversions)
[Implicit Conversions](#ImplicitConversions)

{anchor:ClassDefinition}
# BigRational Class Definition
{code:C#}
/*============================================================
	* Class: BigRational
**
	* Purpose: 
	* --------
	* This class is used to represent an arbitrary precision
	* BigRational number
**
	* A rational number (commonly called a fraction) is a ratio
	* between two integers.  For example (3/6) = (2/4) = (1/2)
**
	* Arithmetic
	* ----------
	* a/b = c/d, iff ad = bc
	* a/b + c/d  == (ad + bc)/bd
	* a/b - c/d  == (ad - bc)/bd
	* a/b % c/d  == (ad % bc)/bd
	* a/b * c/d  == (ac)/(bd)
	* a/b / c/d  == (ad)/(bc)
	* -(a/b)     == (-a)/b
	* (a/b)^(-1) == b/a, if a != 0
**
	* Reduction Algorithm
	* ------------------------
	* Euclid's algorithm is used to simplify the fraction.
	* Calculating the greatest common divisor of two n-digit
	* numbers can be found in
**
	* O(n(log n)^5 (log log n)) steps as n -> +infinity
============================================================*/

namespace Numerics {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Runtime.Serialization;
    using System.Security.Permissions;
    using System.Text;

    [Serializable](Serializable)
    [ComVisible(false)](ComVisible(false))
    public struct BigRational : IComparable, IComparable<BigRational>, IDeserializationCallback, IEquatable<BigRational>, ISerializable {...} //BigRational
} // namespace Numerics
{code:C#}

{anchor:PublicProperties}
# BigRational Public Properties
{code:C#}
        public static BigRational Zero { get; }
        public static BigRational One { get; }
        public static BigRational MinusOne { get; }
        public Int32 Sign { get; }
        public BigInteger Numerator { get; }
        public BigInteger Denominator { get; }
{code:C#}

{anchor:Public Instance Methods}
# BigRational Public Instance Methods
{code:C#}
        // GetWholePart() and GetFractionPart()
        // 
        // BigRational == Whole, Fraction
        //  0/2        ==     0,  0/2
        //  1/2        ==     0,  1/2
        // -1/2        ==     0, -1/2
        //  1/1        ==     1,  0/1
        // -1/1        ==    -1,  0/1
        // -3/2        ==    -1, -1/2
        //  3/2        ==     1,  1/2
        public BigInteger GetWholePart();
        public BigRational GetFractionPart();
        public override bool Equals(Object obj);
        public override int GetHashCode();

        // IComparable
        // Exception: ArgumentException if obj is not a BigRational
        int IComparable.CompareTo(Object obj);

        // IComparable<BigRational>
        public int CompareTo(BigRational other);

        // Object.ToString
        public override String ToString();

        // IEquatable<BigRational>
        // a/b = c/d, iff ad = bc
        public Boolean Equals(BigRational other);
{code:C#}

{anchor:Constructors}
# BigRational Constructors
{code:C#}
        public BigRational(BigInteger numerator);

        // Exception: ArgumentException if value is NaN or an infinity
        public BigRational(Double value);

        // The Decimal type represents floating point numbers exactly, with no rounding error.
        // Values such as "0.1" in Decimal are actually representable, and convert cleanly
        // to BigRational as "11/10"
        // Exception: ArgumentException if the value is an invalid Decimal
        public BigRational(Decimal value);

        //Exception: DivideByZeroException if denominator is 0
        public BigRational(BigInteger numerator, BigInteger denominator);

        //Exception: DivideByZeroException if denominator is 0
        public BigRational(BigInteger whole, BigInteger numerator, BigInteger denominator);
{code:C#}

{anchor:PublicStaticMethods}
# BigRational Public Static Methods
{code:C#}
        public static BigRational Abs(BigRational r);
        public static BigRational Negate(BigRational r);
        public static BigRational Invert(BigRational r);

        public static BigRational Add(BigRational x, BigRational y);
        public static BigRational Subtract(BigRational x, BigRational y);
        public static BigRational Multiply(BigRational x, BigRational y);
        public static BigRational Divide(BigRational dividend, BigRational divisor);

        public static BigRational Remainder(BigRational dividend, BigRational divisor);
        public static BigRational DivRem(BigRational dividend, BigRational divisor, out BigRational remainder);

        // Exception: ArgumentException if baseValue is 0 and exponent is negative
        public static BigRational Pow(BigRational baseValue, BigInteger exponent);

        // Least Common Denominator (LCD)
        //
        // The LCD is the least common multiple of the two denominators.  For instance, the LCD of
        // {1/2, 1/4} is 4 because the least common multiple of 2 and 4 is 4.  Likewise, the LCD
        // of {1/2, 1/3} is 6.
        //       
        // To find the LCD:
        //
        // 1) Find the Greatest Common Divisor (GCD) of the denominators
        // 2) Multiply the denominators together
        // 3) Divide the product of the denominators by the GCD
        public static BigInteger LeastCommonDenominator(BigRational x, BigRational y);

        public static int Compare(BigRational r1, BigRational r2);
{code:C#}

{anchor:OperatorOverloads}
# BigRational Operator Overloads
{code:C#}
        public static bool operator ==(BigRational x, BigRational y);
        public static bool operator !=(BigRational x, BigRational y);

        public static bool operator <(BigRational x, BigRational y);
        public static bool operator <=(BigRational x, BigRational y);
        public static bool operator >(BigRational x, BigRational y);
        public static bool operator >=(BigRational x, BigRational y); 

        public static BigRational operator +(BigRational r);
        public static BigRational operator -(BigRational r);

        public static BigRational operator ++ (BigRational r);
        public static BigRational operator -- (BigRational r);

        public static BigRational operator +(BigRational r1, BigRational r2);
        public static BigRational operator -(BigRational r1, BigRational r2);
        public static BigRational operator *(BigRational r1, BigRational r2);
        public static BigRational operator /(BigRational r1, BigRational r2);
        public static BigRational operator %(BigRational r1, BigRational r2);
{code:C#}

{anchor:ExplicitConversions}
# Explicit Conversions from BigRational to numeric base types
{code:C#}
        // Exception: OverflowException if value is outside the range of a SByte
        [CLSCompliant(false)](CLSCompliant(false))
        public static explicit operator SByte(BigRational value);

        // Exception: OverflowException if value is outside the range of a UInt32
        [CLSCompliant(false)](CLSCompliant(false))
        public static explicit operator UInt16(BigRational value);

        // Exception: OverflowException if value is outside the range of a UInt32
        [CLSCompliant(false)](CLSCompliant(false))
        public static explicit operator UInt32(BigRational value);

        // Exception: OverflowException if value is outside the range of a UInt64
        [CLSCompliant(false)](CLSCompliant(false))
        public static explicit operator UInt64(BigRational value);

        // Exception: OverflowException if value is outside the range of a Byte
        public static explicit operator Byte(BigRational value);

        // Exception: OverflowException if value is outside the range of an Int16
        public static explicit operator Int16(BigRational value);

        // Exception: OverflowException if value is outside the range of an Int32
        public static explicit operator Int32(BigRational value);

        // Exception: OverflowException if value is outside the range of an Int64
        public static explicit operator Int64(BigRational value);

        public static explicit operator BigInteger(BigRational value);

        // The Single value type represents a single-precision 32-bit number with
        // values ranging from negative 3.402823e38 to positive 3.402823e38      
        // Values that do not fit into this range are returned as +/-Infinity
        public static explicit operator Single(BigRational value);

        // The Double value type represents a double-precision 64-bit number with
        // values ranging from -1.79769313486232e308 to +1.79769313486232e308
        // Values that do not fit into this range are returned as +/-Infinity
        public static explicit operator Double(BigRational value);

        // Exception: OverflowException if value is outside the range of a Decimal
        public static explicit operator Decimal(BigRational value);
{code:C#}

{anchor:ImplicitConversions}
# Implicit Conversions from numeric base types to BigRational
{code:C#}
        [CLSCompliant(false)](CLSCompliant(false))
        public static implicit operator BigRational(SByte value);

        [CLSCompliant(false)](CLSCompliant(false))
        public static implicit operator BigRational(UInt16 value);

        [CLSCompliant(false)](CLSCompliant(false))
        public static implicit operator BigRational(UInt32 value);

        [CLSCompliant(false)](CLSCompliant(false))
        public static implicit operator BigRational(UInt64 value);

        public static implicit operator BigRational(Byte value);

        public static implicit operator BigRational(Int16 value);

        public static implicit operator BigRational(Int32 value);

        public static implicit operator BigRational(Int64 value);

        public static implicit operator BigRational(BigInteger value);

        // Exception: ArgumentException if value is NaN or an infinity
        public static implicit operator BigRational(Single value);

        // Exception: ArgumentException if value is NaN or an infinity
        public static implicit operator BigRational(Double value);

        public static implicit operator BigRational(Decimal value);
{code:C#}
