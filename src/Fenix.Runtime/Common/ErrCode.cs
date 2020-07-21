﻿using Fenix.Common.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fenix.Common
{
    [RpcArg("code")]
    public enum DefaultErrCode: Int16
    {
        OK = 0,
        ERROR = -1
    }
}