using System;
using System.Collections;
using System.Collections.Generic;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure
{
    /// <summary>
    /// Projection helpers that reduce observable behavior to a comparable
    /// <see cref="IReadOnlyList{String}"/>. Pair with <see cref="ParityRunner.RunOutcomeParity"/>
    /// to compare two backends' observable output as sequences of strings.
    /// </summary>
    public static class Outcome
    {
        /// <summary>
        /// Reduces the result of <paramref name="func"/> to a single-element list describing the
        /// observable outcome: <c>value:&lt;repr&gt;</c> on success, <c>throw:&lt;ExceptionFullName&gt;</c>
        /// on failure. Pair with <see cref="ParityRunner.RunOutcomeParity"/> when the correct behavior
        /// is not known up front — the reference run determines truth and any difference shows as a
        /// clean mismatch instead of a "reference failed" error.
        /// </summary>
        public static IReadOnlyList<string> Result(Func<object> func)
        {
            try
            {
                return ["value:" + Repr(func())];
            }
            catch (Exception ex)
            {
                return ["throw:" + ex.GetType().FullName];
            }
        }

        /// <summary>Like <see cref="Result"/> but for sequences: success → <c>value:[Type,Type,...]</c>.</summary>
        public static IReadOnlyList<string> ResultMany(Func<IEnumerable> func)
        {
            try
            {
                return ["value:[" + string.Join(",", TypeNames(func())) + "]"];
            }
            catch (Exception ex)
            {
                return ["throw:" + ex.GetType().FullName];
            }
        }

        /// <summary>Ordered simple type-names of a sequence (null → <c>&lt;null&gt;</c>).</summary>
        public static IReadOnlyList<string> TypeNames(IEnumerable sequence)
        {
            var result = new List<string>();
            foreach (var item in sequence)
            {
                result.Add(item == null ? "<null>" : item.GetType().Name);
            }
            return result;
        }

        /// <summary>
        /// Identity order: each argument is mapped to a group index assigned in first-seen order, so
        /// e.g. <c>[0,0,1]</c> means "1st and 2nd are the same instance, 3rd is different". Comparable
        /// across backends without comparing the (always-different) references themselves.
        /// </summary>
        public static IReadOnlyList<string> Order(params object[] instances)
        {
            var map = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var order = new List<string>(instances.Length);
            foreach (var instance in instances)
            {
                var key = instance ?? NullSentinel;
                if (!map.TryGetValue(key, out var id))
                {
                    id = map.Count;
                    map[key] = id;
                }
                order.Add(id.ToString());
            }
            return order;
        }

        private static string Repr(object value)
        {
            if (value == null) return "<null>";
            if (value is string s) return "\"" + s + "\"";
            if (value is bool || value is int || value is long || value.GetType().IsEnum) return value.ToString();
            return value.GetType().Name;
        }

        private static readonly object NullSentinel = new object();
    }
}
