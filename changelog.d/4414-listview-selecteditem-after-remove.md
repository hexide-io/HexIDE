### Fixed

- **Reading a ListView's SelectedItem after ListItems.Remove or Clear no longer stops the program** (#4414). Removing the current item moves the pointer to the item now at its index, or the new last item, without changing other items' selection. Clear and removing the only item leave Nothing, matching measured VB6 behaviour.
