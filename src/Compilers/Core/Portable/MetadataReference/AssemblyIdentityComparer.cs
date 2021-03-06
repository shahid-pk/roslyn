﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Compares assembly identities. 
    /// Derived types may implement platform specific unification and portability policies.
    /// </summary>
    public class AssemblyIdentityComparer
    {
        public static AssemblyIdentityComparer Default { get; } = new AssemblyIdentityComparer();

        public static StringComparer SimpleNameComparer
        {
            get { return StringComparer.OrdinalIgnoreCase; }
        }

        public static StringComparer CultureComparer
        {
            get { return StringComparer.OrdinalIgnoreCase; }
        }

        internal AssemblyIdentityComparer()
        {
        }

        /// <summary>
        /// A set of possible outcomes of <see cref="AssemblyIdentity"/> comparison.
        /// </summary>
        public enum ComparisonResult
        {
            /// <summary>
            /// Reference doesn't match definition.
            /// </summary>
            NotEquivalent = 0,

            /// <summary>
            /// Strongly named reference matches strongly named definition (strong identity is identity with public key or token),
            /// Or weak reference matches weak definition.
            /// </summary>
            Equivalent = 1,

            /// <summary>
            /// Reference matches definition except for version (reference version is lower or higher than definition version).
            /// </summary>
            EquivalentIgnoringVersion = 2
        }

        /// <summary>
        /// Compares assembly reference name (possibly partial) with definition identity.
        /// </summary>
        /// <param name="referenceDisplayName">Partial or full assembly display name.</param>
        /// <param name="definition">Full assembly display name.</param>
        /// <returns>True if the reference name matches the definition identity.</returns>
        public bool ReferenceMatchesDefinition(string referenceDisplayName, AssemblyIdentity definition)
        {
            bool unificationApplied;
            return Compare(null, referenceDisplayName, definition, out unificationApplied, ignoreVersion: false) != ComparisonResult.NotEquivalent;
        }

        /// <summary>
        /// Compares assembly reference identity with definition identity.
        /// </summary>
        /// <param name="reference">Reference assembly identity.</param>
        /// <param name="definition">Full assembly display name.</param>
        /// <returns>True if the reference identity matches the definition identity.</returns>
        public bool ReferenceMatchesDefinition(AssemblyIdentity reference, AssemblyIdentity definition)
        {
            bool unificationApplied;
            return Compare(reference, null, definition, out unificationApplied, ignoreVersion: false) != ComparisonResult.NotEquivalent;
        }

        /// <summary>
        /// Compares reference assembly identity with definition identity and returns their relationship.
        /// </summary>
        /// <param name="reference">Reference identity.</param>
        /// <param name="definition">Definition identity.</param>
        public ComparisonResult Compare(AssemblyIdentity reference, AssemblyIdentity definition)
        {
            bool unificationApplied;
            return Compare(reference, null, definition, out unificationApplied, ignoreVersion: true);
        }

        // internal for testing
        internal ComparisonResult Compare(AssemblyIdentity reference, string referenceDisplayName, AssemblyIdentity definition, out bool unificationApplied, bool ignoreVersion)
        {
            Debug.Assert((reference != null) ^ (referenceDisplayName != null));
            unificationApplied = false;
            AssemblyIdentityParts parts;

            if (reference != null)
            {
                // fast path
                bool? eq = TriviallyEquivalent(reference, definition);
                if (eq.HasValue)
                {
                    return eq.Value ? ComparisonResult.Equivalent : ComparisonResult.NotEquivalent;
                }

                parts = AssemblyIdentityParts.Name | AssemblyIdentityParts.Version | AssemblyIdentityParts.Culture | AssemblyIdentityParts.PublicKeyToken;
            }
            else
            {
                if (!AssemblyIdentity.TryParseDisplayName(referenceDisplayName, out reference, out parts) ||
                    reference.ContentType != definition.ContentType)
                {
                    return ComparisonResult.NotEquivalent;
                }
            }

            Debug.Assert(reference.ContentType == definition.ContentType);

            bool isFxAssembly;
            if (!ApplyUnificationPolicies(ref reference, ref definition, parts, out isFxAssembly))
            {
                return ComparisonResult.NotEquivalent;
            }

            if (ReferenceEquals(reference, definition))
            {
                return ComparisonResult.Equivalent;
            }

            bool compareCulture = (parts & AssemblyIdentityParts.Culture) != 0;
            bool comparePublicKeyToken = (parts & AssemblyIdentityParts.PublicKeyOrToken) != 0;

            if (!definition.IsStrongName)
            {
                if (reference.IsStrongName)
                {
                    return ComparisonResult.NotEquivalent;
                }

                if (!AssemblyIdentity.IsFullName(parts))
                {
                    if (!SimpleNameComparer.Equals(reference.Name, definition.Name))
                    {
                        return ComparisonResult.NotEquivalent;
                    }

                    if (compareCulture && !CultureComparer.Equals(reference.CultureName, definition.CultureName))
                    {
                        return ComparisonResult.NotEquivalent;
                    }

                    // version is ignored

                    return ComparisonResult.Equivalent;
                }

                isFxAssembly = false;
            }

            if (!SimpleNameComparer.Equals(reference.Name, definition.Name))
            {
                return ComparisonResult.NotEquivalent;
            }

            if (compareCulture && !CultureComparer.Equals(reference.CultureName, definition.CultureName))
            {
                return ComparisonResult.NotEquivalent;
            }

            if (comparePublicKeyToken && !AssemblyIdentity.KeysEqual(reference, definition))
            {
                return ComparisonResult.NotEquivalent;
            }

            bool hasSomeVersionParts = (parts & AssemblyIdentityParts.Version) != 0;
            bool hasPartialVersion = (parts & AssemblyIdentityParts.Version) != AssemblyIdentityParts.Version;

            // If any version parts were specified then compare the versions. The comparison fails if some version parts are missing.
            if (definition.IsStrongName &&
                hasSomeVersionParts &&
                (hasPartialVersion || reference.Version != definition.Version))
            {
                if (isFxAssembly)
                {
                    unificationApplied = true;
                    return ComparisonResult.Equivalent;
                }

                if (ignoreVersion)
                {
                    return ComparisonResult.EquivalentIgnoringVersion;
                }

                return ComparisonResult.NotEquivalent;
            }

            return ComparisonResult.Equivalent;
        }

        private static bool? TriviallyEquivalent(AssemblyIdentity x, AssemblyIdentity y)
        {
            // Identities from different binding models never match.
            if (x.ContentType != y.ContentType)
            {
                return false;
            }

            // Can't compare if identity might get retargeted.
            if (x.IsRetargetable || y.IsRetargetable)
            {
                return null;
            }

            return AssemblyIdentity.MemberwiseEqual(x, y);
        }

        internal virtual bool ApplyUnificationPolicies(ref AssemblyIdentity reference, ref AssemblyIdentity definition, AssemblyIdentityParts referenceParts, out bool isFxAssembly)
        {
            isFxAssembly = false;
            return true;
        }
    }
}
