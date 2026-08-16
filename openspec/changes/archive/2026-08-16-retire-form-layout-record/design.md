# Design record — what the Form Layout window is

Preserved because the code still exists and someone will eventually read it, or remove it, and will want to
know what it was meant to do.

## What it shows

A raised monitor illustration containing a black screen, with a small white rectangle standing for the form.
The illustration is constrained to the **primary display's actual aspect ratio**, read from the window
system rather than assumed — the original implementation hardcoded 4:3, which was right for the era the
window comes from and wrong on any modern screen.

The rectangle's position and size are derived from the form's own properties rather than being decorative.
The form's dimensions are in twips, so they are converted to a fraction of the screen and then scaled to
whatever size the illustration has been laid out at, which means the thumbnail stays correct when the tool
window is resized.

Where the form's startup position is set explicitly, the thumbnail sits at those coordinates. Where it is
set to centre — on the screen or on its owner — the thumbnail is drawn centred, because both resolve to the
middle of the one screen this illustration depicts. Where it is left to the window manager, the thumbnail is
placed in the upper-left region, which approximates where such a window tends to land without pretending to
predict it.

## Why it is not being kept

The window's premise is that a developer chooses, at design time, where a window will appear on the user's
screen. Every part of that premise has weakened: the screen is often not one screen, the window manager
frequently overrides the choice, and the developer's display is rarely the user's. The startup-position
property remains meaningful — centre-on-screen is still a sensible thing to ask for — but choosing it from a
dropdown is enough. The picture is the part that has aged.

There is a second reason, specific to this project. The illustration is a fixed piece of chrome occupying a
dock slot in the default workspace. Space in the default layout is the scarcest thing the IDE has, and it is
spent on surfaces a developer uses in most sessions. This is not one of them.

## What removal will have to account for

Not just the tool window and its view model: the View menu entry, the Standard toolbar button, the routed
command behind both, the slot it occupies in the default dock layout, and the localization keys for its
caption and menu text — which exist in every shipped language pack and would otherwise become orphans the
coverage check keeps alive forever.

## If it returns

As an add-in. It is a self-contained tool window driven entirely by properties the host already exposes,
which makes it a better demonstration of the add-in surface than the surface's own sample — it needs a
dockable window, project state, and change notifications, and nothing else.
