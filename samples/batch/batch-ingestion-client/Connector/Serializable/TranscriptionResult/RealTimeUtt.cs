// <copyright file="RealTimeUtt.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
// </copyright>
namespace Connector
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class RealTimeUtt
    {
        public string DisplayText { get; set; }

        public int Duration { get; set; }

        public string Id { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only
        public List<NBest> NBest { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only

        public int Offset { get; set; }

        public string RecognitionStatus { get; set; }
    }
}