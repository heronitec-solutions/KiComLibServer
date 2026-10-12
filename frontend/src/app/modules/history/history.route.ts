import { ChangeDetectionStrategy, Component } from '@angular/core';
import { HistoryModule } from './history.module';

@Component({
    selector: 'commit-table-route',
    imports: [HistoryModule],
    template: '<commit-table></commit-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class CommitTableRouteComponent {}

@Component({
    selector: 'commit-detail-route',
    imports: [HistoryModule],
    template: '<commit-detail></commit-detail>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class CommitDetailRouteComponent {}
