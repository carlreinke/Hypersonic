//
// Copyright (C) 2018  Carl Reinke
//
// This file is part of Hypersonic.
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
// even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If
// not, see <https://www.gnu.org/licenses/>.
//
using System;
using System.Collections.Generic;
using System.Text;

namespace Hypersonic.Ffmpeg
{
    internal class ArgumentList : List<string>
    {
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        public new ArgumentList Add(string argument)
        {
            if (argument == null)
                throw new ArgumentNullException(nameof(argument));

            base.Add(argument);
            return this;
        }

        public new ArgumentList AddRange(IEnumerable<string> arguments)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            base.AddRange(arguments);
            return this;
        }

        public ArgumentList Add(Action<StringBuilder> argumentBuilder)
        {
            if (argumentBuilder == null)
                throw new ArgumentNullException(nameof(argumentBuilder));

            argumentBuilder(_stringBuilder);
            Add(_stringBuilder.ToString());
            _stringBuilder.Clear();
            return this;
        }
    }
}
