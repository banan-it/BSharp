﻿using BSharp.Controllers.DTO;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace BSharp.Services.Utilities
{
    public static class Extensions
    {
        /// <summary>
        /// Checks whether a certain type has a certain property name defined
        /// </summary>
        public static bool HasProperty(this Type type, string propertyName)
        {
            return type.GetProperty(propertyName) != null;
        }

        /// <summary>
        /// Retrieves the username of the authenticated claims principal
        /// </summary>
        public static string UserId(this ClaimsPrincipal user)
        {
            return "4F7785F2-5942-4CFB-B5AD-85AB72F7EB35"; // TODO
        }

        /// <summary>
        /// Extracts all errors inside an IdentityResult and concatenates them together, 
        /// falling back to a default message if no errors were found in the IdentityResult object
        /// </summary>
        public static string ErrorMessage(this IdentityResult result, string defaultMessage)
        {
            string errorMessage = defaultMessage;
            if (result.Errors.Any())
                errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));

            return errorMessage;
        }

        /// <summary>
        /// Creates a dictionary that maps each entity to its index in the list,
        /// this is a much faster alternative to <see cref="List{T}.IndexOf(T)"/>
        /// if it is expected that it will be performed N times, since it performs 
        /// a linear search resulting in O(N^2) complexity
        /// </summary>
        public static Dictionary<T, int> ToIndexDictionary<T>(this List<T> @this)
        {
            if (@this == null)
            {
                throw new ArgumentNullException(nameof(@this));
            }

            var result = new Dictionary<T, int>(@this.Count);
            for (int i = 0; i < @this.Count; i++)
            {
                var entity = @this[i];
                result[entity] = i;
            }

            return result;
        }

        public static void TrimStringProperties(this DtoForSaveBase entity)
        {
            var dtoType = entity.GetType();
            foreach(var prop in dtoType.GetProperties())
            {
                if(prop.PropertyType == typeof(string))
                {
                    var originalValue = prop.GetValue(entity)?.ToString();
                    if(originalValue != null)
                    {
                        var trimmed = originalValue.Trim();
                        prop.SetValue(entity, trimmed);
                    }
                }
                else if (prop.PropertyType.IsSubclassOf(typeof(DtoForSaveBase)))
                {
                    var dtoForSave = prop.GetValue(entity);
                    if(dtoForSave != null)
                    {
                        (dtoForSave as DtoForSaveBase).TrimStringProperties();
                    }
                }
                else
                {
                    var isDtoList = prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>);
                    if (isDtoList)
                    {
                        // TODO trim all children in a navigation collection
                        throw new NotImplementedException("Trimming navigation collection is not implemented yet");
                    }
                }
            }
        }
    }
}
