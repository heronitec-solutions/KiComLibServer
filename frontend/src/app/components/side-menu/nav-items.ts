export interface NavItem {
  title: string;
  /** Material icon ligature; omitted routes render as a disabled placeholder. */
  icon: string;
  route?: string;
  /** Shown only while an administrator is signed in. */
  adminOnly?: boolean;
  /** Shown only when the account may read the warehouse. */
  requiresWarehouse?: boolean;
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

export const NAV_GROUPS: NavGroup[] = [
  {
    label: 'Main Menu',
    items: [
      { title: 'Parts', icon: 'inventory_2', route: '/parts' },
      { title: 'Warehouse', icon: 'warehouse', route: '/warehouse', requiresWarehouse: true },
      { title: 'Projects', icon: 'developer_board', route: '/projects' },
      { title: 'Manufacturers', icon: 'business', route: '/manufacturers' },
      { title: 'Distributors', icon: 'local_shipping', route: '/distributors' },
      { title: 'Libraries', icon: 'collections_bookmark', route: '/libraries' },
      { title: 'Library Files', icon: 'storage', route: '/library_files' },
      { title: 'Datasheets', icon: 'description', route: '/datasheets' },
      { title: 'CAD Models', icon: 'view_in_ar', route: '/cad_models' },
      { title: 'Symbols', icon: 'bolt', route: '/symbols' },
      { title: 'Footprints', icon: 'memory', route: '/footprints' },
      { title: 'History', icon: 'history', route: '/commits' },
    ],
  },
  {
    label: 'Administration',
    items: [
      { title: 'User Management', icon: 'group', route: '/users', adminOnly: true },
      { title: 'User Groups', icon: 'shield', route: '/groups', adminOnly: true },
      { title: 'Directory', icon: 'corporate_fare', route: '/directory', adminOnly: true },
      { title: 'Backups', icon: 'backup', route: '/backups', adminOnly: true },
      { title: 'Settings', icon: 'tune', route: '/settings', adminOnly: true },
      { title: 'APIs', icon: 'cloud', route: '/apis', adminOnly: true },
      { title: 'Git Integration', icon: 'hub', route: '/git', adminOnly: true },
    ],
  },
];
