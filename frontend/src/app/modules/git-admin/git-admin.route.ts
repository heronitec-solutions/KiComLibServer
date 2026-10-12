import { ChangeDetectionStrategy, Component } from '@angular/core';
import { GitAdminModule } from './git-admin.module';

@Component({
    selector: 'git-admin-page-route',
    imports: [GitAdminModule],
    template: '<git-admin-page></git-admin-page>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class GitAdminPageRouteComponent {}
