﻿//
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the MIT License. See License.txt in the project root for license information.
//
using System.Windows;
using AppKit;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.VisualStudio.Text.Differencing
{
    /// <summary>
    /// Callback used with <see cref="ICocoaDifferenceViewerFactoryService"/> to create a text view host.
    /// </summary>
    /// <param name="textViewModel">The text view model to use in creating the text view.</param>
    /// <param name="roles">The roles specific to this view.</param>
    /// <param name="options">The options to use in creating the text view.</param>
    /// <param name="visualElement">The top-level visual element for this host.</param>
    /// <param name="textViewHost">The created text view host.</param>
    /// <remarks>
    /// <para>
    /// To get standard text view roles, the implementation of this method should concatenate the given <paramref name="roles"/> with
    /// <see cref="ITextEditorFactoryService.DefaultRoles"/>.
    /// </para>
    /// <para>
    /// In most cases, the visual element can just be the <paramref name="textViewHost"/>'s <see cref="ICocoaTextViewHost.HostControl"/>.
    /// </para>
    /// </remarks>
    public delegate void CreateTextViewHostCallback(IDifferenceTextViewModel textViewModel, ITextViewRoleSet roles, IEditorOptions options,
                                                    out NSView visualElement, out ICocoaTextViewHost textViewHost);
}
