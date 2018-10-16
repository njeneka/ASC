using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace ASC.DataAccess.Extensions
{
    public static class ObjectExtensions
    {
        public static T CopyObject<T>(this object objSource)
        {
            var serialized = JsonConvert.SerializeObject(objSource);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }
}
