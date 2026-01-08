using Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class Category : BaseEntity, ISoftDelete
    {
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public string Name { get; set; } = default!;

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
    }

}
