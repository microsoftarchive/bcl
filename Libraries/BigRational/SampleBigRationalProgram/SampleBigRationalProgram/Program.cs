//   Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using Numerics;
using System.Numerics;

namespace SampleBigRationalProgram
{
    enum RationalField
    {
        FirstNumerator,
        FirstDenominator,
        SecondNumerator,
        SecondDenominator
    };

    enum RationalOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        LeastCommonDenominator
    };

    class Program
    {
        // simple demo which reads in two rationals and an operator then prints the result
        static void Main(string[] args)
        {
            ConsoleProlog();
            while (true)
            {
                BigInteger numA = GetNumber(RationalField.FirstNumerator);
                BigInteger numB = GetNumber(RationalField.FirstDenominator);
                BigInteger numC = GetNumber(RationalField.SecondNumerator);
                BigInteger numD = GetNumber(RationalField.SecondDenominator);
                BigRational rational1 = new BigRational(numA, numB);
                BigRational rational2 = new BigRational(numC, numD);

                RationalOperator op = GetOperator();
                PerformOperationAndShowResult(rational1, rational2, op);
            }
        }

        static void ConsoleProlog()
        {
            Console.WriteLine("Microsoft (R) SampleBigRationalProgram.  Version 1.0.00000.0");
            Console.WriteLine("Copyright (c) Microsoft Corporation.  All rights reserved.");
            Console.WriteLine();
            Console.WriteLine("Press control+C to terminate the demo at any time.");
            Console.WriteLine();
        }

        static BigInteger GetNumber(RationalField field)
        {
            string fieldString;
            switch (field)
            {
                case RationalField.FirstNumerator:
                    fieldString = "first numerator:    ";
                    break;
                case RationalField.FirstDenominator:
                    fieldString = "first denominator:  ";
                    break;
                case RationalField.SecondNumerator:
                    fieldString = "second numerator:   ";
                    break;
                case RationalField.SecondDenominator:
                    fieldString = "second denominator: ";
                    break;
                default:
                    throw new InvalidOperationException();
            }
            Console.Write("Enter the {0} ", fieldString);
            
            string input = Console.ReadLine();
            BigInteger result;
            if (!BigInteger.TryParse(input, out result))
            {
                Console.WriteLine("Error: unable to parse value.  Defaulting to one (1) for the demo.");
                result = BigInteger.One;
            }
            return result;
        }

        static RationalOperator GetOperator()
        {
            string op;
            Console.Write("Enter the operator [+, -, *, /, lcd]: ");
            op = Console.ReadLine().ToLowerInvariant();

            switch (op)
            {
                case "+":
                    return RationalOperator.Add;
                case "-":
                    return RationalOperator.Subtract;
                case "*":
                    return RationalOperator.Multiply;
                case "/":
                    return RationalOperator.Divide;
                case "lcd":
                    return RationalOperator.LeastCommonDenominator;
                default:
                    Console.WriteLine("Error: unknown operator, defaulting to addition (+) for the demo.");
                    return RationalOperator.Add;
            }
        }

        static void PerformOperationAndShowResult(BigRational x, BigRational y, RationalOperator op)
        {
            switch (op)
            {
                case RationalOperator.Add:
                    Console.WriteLine("{0} + {1} = {2}", x, y, x + y);
                    break;
                case RationalOperator.Divide:
                    Console.WriteLine("{0} / {1} = {2}", x, y, x / y);
                    break;
                case RationalOperator.LeastCommonDenominator:
                    Console.WriteLine("LeastCommonDenominator({0}, {1}) = {2}", x, y, BigRational.LeastCommonDenominator(x, y));
                    break;
                case RationalOperator.Multiply:
                    Console.WriteLine("{0} * {1} = {2}", x, y, x * y);
                    break;
                case RationalOperator.Subtract:
                    Console.WriteLine("{0} - {1} = {2}", x, y, x - y);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
