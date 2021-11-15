using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace signals_hub.Interfaces
{
    public interface IServiceFactory
    {
        T CreateInjectableService<T>(object[] optionalParams = null);
    }

    public interface Injectable
    {

    }
}
