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
 * Copyright (c) 2020 The Dotnetty-Span-Fork Project (cuteant@outlook.com) All rights reserved.
 *
 *   https://github.com/cuteant/dotnetty-span-fork
 *
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

namespace DotNetty.Codecs.Http2
{
    using DotNetty.Buffers;

    /// <summary>
    /// HTTP/2 DATA frame.
    /// </summary>
    public interface IHttp2DataFrame : IHttp2StreamFrame, IByteBufferHolder
    {
        /// <summary>
        /// Frame padding to use. Will be non-negative and less than 256.
        /// </summary>
        /// <returns></returns>
        int Padding { get; }

        /// <summary>
        /// Returns the number of bytes that are flow-controlled initially, so even if the <see cref="IByteBufferHolder.Content"/> is consumed
        /// this will not change.
        /// </summary>
        /// <returns></returns>
        int InitialFlowControlledBytes { get; }

        /// <summary>
        /// Returns <c>true</c> if the END_STREAM flag ist set.
        /// </summary>
        /// <returns></returns>
        bool IsEndStream { get; }
    }
}
