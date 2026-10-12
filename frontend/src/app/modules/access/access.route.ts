import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AccessModule } from './access.module';

@Component({
    selector: 'account-page-route',
    imports: [AccessModule],
    template: '<account-page></account-page>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class AccountPageRouteComponent {}

@Component({
    selector: 'user-table-route',
    imports: [AccessModule],
    template: '<user-table></user-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class UserTableRouteComponent {}

@Component({
    selector: 'group-table-route',
    imports: [AccessModule],
    template: '<group-table></group-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class GroupTableRouteComponent {}

@Component({
    selector: 'directory-page-route',
    imports: [AccessModule],
    template: '<directory-page></directory-page>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class DirectoryPageRouteComponent {}

@Component({
    selector: 'backup-page-route',
    imports: [AccessModule],
    template: '<backup-page></backup-page>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class BackupPageRouteComponent {}

@Component({
    selector: 'settings-page-route',
    imports: [AccessModule],
    template: '<settings-page></settings-page>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class SettingsPageRouteComponent {}

@Component({
    selector: 'apis-page-route',
    imports: [AccessModule],
    template: '<apis-page></apis-page>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class ApisPageRouteComponent {}
