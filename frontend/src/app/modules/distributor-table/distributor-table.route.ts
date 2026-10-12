import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DistributorTableModule } from './distributor-table.module';

@Component({
    selector: 'distributor-table-route',
    imports: [DistributorTableModule],
    template: '<distributor-table></distributor-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class DistributorTableRouteComponent {}
