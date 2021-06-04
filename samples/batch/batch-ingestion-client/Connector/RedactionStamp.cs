// <copyright file="RedactionStamp.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
// </copyright>

namespace Connector
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    internal class RedactionStamp
    {
        public RedactionStamp(long offset, long duration)
        {
            this.Offset = offset;
            this.Duration = duration;
        }

        /// <summary>
        /// Gets or sets the offset.
        /// </summary>
        internal long Offset { get; set; }

        /// <summary>
        /// Gets or sets the duration.
        /// </summary>
        internal long Duration { get; set; }
    }
}
