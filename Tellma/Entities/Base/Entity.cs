﻿using System.ComponentModel.DataAnnotations.Schema;

namespace Tellma.Entities
{
    /// <summary>
    /// All entities in <see cref="Entities"/> derive from this base class
    /// </summary>
    public class Entity
    {
        private EntityMetadata _entityMetadata;

        /// <summary>
        /// Contains metadata about the entity for client side consumption
        /// </summary>
        [NotMapped]
        public EntityMetadata EntityMetadata
        {
            get {  return _entityMetadata ??= new EntityMetadata(); }
            set { _entityMetadata = value; }
        }
    }
}
