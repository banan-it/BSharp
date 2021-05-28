﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Tellma.Utilities.Common;

namespace Tellma.Api.Templating
{
    /// <summary>
    /// Represents an OR expression, e.g. A || B. <br/> 
    /// Always evaluated to a boolean value.
    /// </summary>
    public class TemplexDisjunction : TemplexBase
    {
        /// <summary>
        /// The left operand.
        /// </summary>
        public TemplexBase Left { get; set; }

        /// <summary>
        /// The right operand.
        /// </summary>
        public TemplexBase Right { get; set; }

        public override async IAsyncEnumerable<Path> ComputeSelect(EvaluationContext ctx)
        {
            await foreach (var select in Left.ComputeSelect(ctx))
            {
                yield return select;
            }

            await foreach (var select in Right.ComputeSelect(ctx))
            {
                yield return select;
            }
        }

        public override IAsyncEnumerable<Path> ComputePaths(EvaluationContext ctx)
        {
            return AsyncUtil.Empty<Path>();
        }

        public override async Task<object> Evaluate(EvaluationContext ctx)
        {
            var left = await Left.Evaluate(ctx) ?? false; // Null is treated as false
            var right = await Right.Evaluate(ctx) ?? false; // Null is treated as false

            if (left is not bool boolLeft)
            {
                throw new TemplateException($"Operator '||' could not be applied. The expression ({Left}) does not evaluate to a boolean value.");
            }
            else if (right is not bool boolRight)
            {
                throw new TemplateException($"Operator '||' could not be applied. The expression ({Right}) does not evaluate to a boolean value.");
            }
            else
            {
                return boolLeft || boolRight;
            }
        }

        public override string ToString()
        {
            return $"{Left} OR {Right}";
        }

        /// <summary>
        /// Creates a new <see cref="TemplexDisjunction"/>.
        /// </summary>
        public static TemplexDisjunction Make(TemplexBase left, TemplexBase right)
        {
            return new TemplexDisjunction
            {
                Left = left,
                Right = right,
            };
        }
    }
}
