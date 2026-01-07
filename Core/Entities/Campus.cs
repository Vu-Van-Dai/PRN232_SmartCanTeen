using Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class Campus : BaseEntity
    {
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;

        public string? Config { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
