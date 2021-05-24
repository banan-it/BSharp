﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tellma.Repository.Common
{
    public static class SqlParameterCollectionExtensions
    {
        /// <summary>
        /// Extension method that adds <see cref="DBNull.Value"/> when the supplied value
        /// is null, instead of the default behavior of not adding anything at all
        /// </summary>
        public static SqlParameter Add(this SqlParameterCollection target, string parameterName, object value)
        {
            return target.AddWithValue(parameterName, value ?? DBNull.Value);
        }
    }
}
