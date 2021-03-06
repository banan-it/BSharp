﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Tellma.Controllers.Utilities
{
    public class MaskTrieComparer : IEqualityComparer<MaskTrie>
    {
        public bool Equals(MaskTrie x, MaskTrie y)
        {
            // either they are the exact same mask, or they have identical structure
            return x == y || (
                x != null && 
                y != null && 
                x.Count == y.Count && x.Keys.All(key => y.ContainsKey(key) && 
                Equals(x[key], y[key])));
        }

        public int GetHashCode(MaskTrie obj)
        {
            return obj.Select(e => e.Key.GetHashCode() ^ e.Value.GetHashCode())
                .Aggregate(1990, (e1, e2) => e1 ^ e2);
        }
    }
}
