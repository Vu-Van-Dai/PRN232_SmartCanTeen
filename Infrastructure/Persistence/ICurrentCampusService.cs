using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence
{
    public interface ICurrentCampusService
    {
        Guid CampusId { get; }
    }
}
