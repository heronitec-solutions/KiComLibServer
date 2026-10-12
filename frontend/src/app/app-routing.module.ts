import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ContentAreaComponent } from './components/content-area/content-area.component';
import { authGuard } from './services/auth.guard';

const routes: Routes = [
  {
    path: '',
    component: ContentAreaComponent,
    canActivateChild: [authGuard],
    children: [
      {
        path: 'login',
        loadComponent: () => import('./components/login-page/login-page.component').then(m => m.LoginPageComponent),
        data: { title: 'Sign in' }
      },
      {
        path: 'home',
        loadComponent: () => import('./components/welcome-page/welcome-page.component').then(m => m.WelcomePageComponent),
        data: { title: 'Welcome' }
      },
      {
        path: 'distributors',
        loadComponent: () => import('./modules/distributor-table/distributor-table.route').then(m => m.DistributorTableRouteComponent),
        data: { title: 'Distributors' }
      },
      {
        path: 'manufacturers',
        loadComponent: () => import('./modules/manufacturer-table/manufacturer-table.route').then(m => m.ManufacturerTableRouteComponent),
        data: { title: 'Manufacturers' }
      },
      {
        path: 'library_files',
        loadComponent: () => import('./modules/library-file-table/library-file-table.route').then(m => m.LibraryFileTableRouteComponent),
        data: { title: 'Library Files' }
      },
      {
        path: 'libraries',
        loadComponent: () => import('./modules/library-table/library-table.route').then(m => m.LibraryTableRouteComponent),
        data: { title: 'Libraries' }
      },
      {
        path: 'datasheets',
        loadComponent: () => import('./modules/datasheet-table/datasheet-table.route').then(m => m.DatasheetTableRouteComponent),
        data: { title: 'Datasheets' }
      },
      {
        path: 'cad_models',
        loadComponent: () => import('./modules/cad-model-table/cad-model-table.route').then(m => m.CadModelTableRouteComponent),
        data: { title: 'CAD Models' }
      },
      {
        path: 'symbols',
        loadComponent: () => import('./modules/symbol-table/symbol-table.route').then(m => m.SymbolTableRouteComponent),
        data: { title: 'Symbols' }
      },
      {
        path: 'footprints',
        loadComponent: () => import('./modules/footprint-table/footprint-table.route').then(m => m.FootprintTableRouteComponent),
        data: { title: 'Footprints' }
      },
      {
        path: 'parts',
        loadComponent: () => import('./modules/part-table/part-table.route').then(m => m.PartTableRouteComponent),
        data: { title: 'Parts' }
      },
      {
        path: 'warehouse',
        loadComponent: () => import('./modules/warehouse/warehouse.route').then(m => m.WarehouseTableRouteComponent),
        data: { title: 'Warehouse', warehouse: true }
      },
      {
        path: 'projects',
        loadComponent: () => import('./modules/projects/projects.route').then(m => m.ProjectTableRouteComponent),
        data: { title: 'Projects' }
      },
      {
        path: 'projects/:id/diff',
        loadComponent: () => import('./modules/projects/projects.route').then(m => m.VisualDiffRouteComponent),
        data: { title: 'Visual diff' }
      },
      {
        path: 'projects/:id',
        loadComponent: () => import('./modules/projects/projects.route').then(m => m.ProjectDetailRouteComponent),
        data: { title: 'Project' }
      },
      {
        path: 'commits',
        loadComponent: () => import('./modules/history/history.route').then(m => m.CommitTableRouteComponent),
        data: { title: 'History' }
      },
      {
        path: 'commits/:id',
        loadComponent: () => import('./modules/history/history.route').then(m => m.CommitDetailRouteComponent),
        data: { title: 'Commit' }
      },
      {
        path: 'account',
        loadComponent: () => import('./modules/access/access.route').then(m => m.AccountPageRouteComponent),
        data: { title: 'My Account' }
      },
      {
        path: 'info',
        loadComponent: () => import('./components/info-page/info-page.component').then(m => m.InfoPageComponent),
        data: { title: 'Info' }
      },
      {
        path: 'users',
        loadComponent: () => import('./modules/access/access.route').then(m => m.UserTableRouteComponent),
        data: { title: 'User Management', adminOnly: true }
      },
      {
        path: 'groups',
        loadComponent: () => import('./modules/access/access.route').then(m => m.GroupTableRouteComponent),
        data: { title: 'User Groups', adminOnly: true }
      },
      {
        path: 'directory',
        loadComponent: () => import('./modules/access/access.route').then(m => m.DirectoryPageRouteComponent),
        data: { title: 'Directory', adminOnly: true }
      },
      {
        path: 'backups',
        loadComponent: () => import('./modules/access/access.route').then(m => m.BackupPageRouteComponent),
        data: { title: 'Backups', adminOnly: true }
      },
      {
        path: 'settings',
        loadComponent: () => import('./modules/access/access.route').then(m => m.SettingsPageRouteComponent),
        data: { title: 'Settings', adminOnly: true }
      },
      {
        path: 'apis',
        loadComponent: () => import('./modules/access/access.route').then(m => m.ApisPageRouteComponent),
        data: { title: 'APIs', adminOnly: true }
      },
      {
        path: 'git',
        loadComponent: () => import('./modules/git-admin/git-admin.route').then(m => m.GitAdminPageRouteComponent),
        data: { title: 'Git Integration', adminOnly: true }
      },

      // Default and wildcard route:
      { path: '', redirectTo: 'home', pathMatch: 'full' },
      { path: '**', redirectTo: 'home' }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forRoot(routes, {onSameUrlNavigation: 'reload'})],
  exports: [RouterModule]
})
export class AppRoutingModule { }
