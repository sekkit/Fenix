﻿/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 * Copyright (c) The DotNetty Project (Microsoft). All rights reserved.
 *
 *   https://github.com/azure/dotnetty
 *
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 *
 * Copyright (c) 2020 The Dotnetty-Span-Fork Project (cuteant@outlook.com) All rights reserved.
 *
 *   https://github.com/cuteant/dotnetty-span-fork
 *
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

namespace DotNetty.Common
{
    using System.Collections.Generic;

    public class ThreadLocalList<T> : List<T>
    {
        const int DefaultInitialCapacity = 8;

        static readonly ThreadLocalPool<ThreadLocalList<T>> Pool = new ThreadLocalPool<ThreadLocalList<T>>(handle => new ThreadLocalList<T>(handle));

        readonly ThreadLocalPool.Handle returnHandle;

        ThreadLocalList(ThreadLocalPool.Handle returnHandle)
        {
            this.returnHandle = returnHandle;
        }

        public static ThreadLocalList<T> NewInstance() => Pool.Take();

        public static ThreadLocalList<T> NewInstance(int minCapacity)
        {
            var ret = Pool.Take();
            if (ret.Capacity < minCapacity)
            {
                ret.Capacity = minCapacity;
            }
            return ret;

        }

        public void Return()
        {
            this.Clear();
            this.returnHandle.Release(this);
        }
    }
}