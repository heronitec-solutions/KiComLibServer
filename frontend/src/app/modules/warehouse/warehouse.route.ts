import { ChangeDetectionStrategy, Component } from '@angular/core';
import { WarehouseModule } from './warehouse.module';

@Component({
    selector: 'warehouse-table-route',
    imports: [WarehouseModule],
    template: '<warehouse-table></warehouse-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class WarehouseTableRouteComponent {}
