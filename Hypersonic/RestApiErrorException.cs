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

namespace Hypersonic
{
#pragma warning disable CA1064 // Exceptions should be public
#pragma warning disable CA1032 // Implement standard exception constructors
    internal sealed class RestApiErrorException : Exception
#pragma warning restore CA1032 // Implement standard exception constructors
#pragma warning restore CA1064 // Exceptions should be public
    {
        public RestApiErrorException(int code, string message)
            : base(message)
        {
            Code = code;
        }

        public int Code { get; }

        public static RestApiErrorException GenericError(string message)
        {
            return new RestApiErrorException(0, message ?? "An error occurred.");
        }

        public static RestApiErrorException RequiredParameterMissingError(string parameter)
        {
            return new RestApiErrorException(10, $"Required parameter '{parameter}' is missing.");
        }

        public static RestApiErrorException ClientMustUpgradeError()
        {
            return new RestApiErrorException(20, "Incompatible Subsonic REST protocol version.  Client must upgrade.");
        }

        public static RestApiErrorException ServerMustUpgradeError()
        {
            return new RestApiErrorException(30, "Incompatible Subsonic REST protocol version.  Server must upgrade.");
        }

        public static RestApiErrorException WrongUsernameOrPassword()
        {
            return new RestApiErrorException(40, "Wrong username or password.");
        }

        public static RestApiErrorException UserNotAuthorizedError()
        {
            return new RestApiErrorException(50, "The user is not authorized for the given operation.");
        }

        public static RestApiErrorException DataNotFoundError()
        {
            return new RestApiErrorException(70, "The requested data was not found.");
        }
    }
}
