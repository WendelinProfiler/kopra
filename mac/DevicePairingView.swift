import SwiftUI
import AppKit
import ApplicationServices

struct DevicePairingView:View {
    @ObservedObject var model:AppModel
    @State var cameraError=""
    @State var numericCode=""
    @State var confirmDisconnect=false
    var body:some View {
        Section(L.text("Gemeinsame Zwischenablage")){
            Label(model.paired ? L.text("Windows verbunden"):model.pairingInProgress ? L.text("Verbindung einrichten"):L.text("Windows-PC verbinden"),systemImage:model.paired ? "checkmark.circle.fill":"laptopcomputer")
            if model.paired { Text(L.text(model.status)).font(.caption).foregroundStyle(.secondary) }
            if !model.paired {
                if !model.pairingInProgress{
                    Text(L.text("Öffne auf dem Windows-PC Kopra → Einstellungen → Geräte → Mac verbinden.")).font(.caption).foregroundStyle(.secondary)
                    HStack {
                        TextField(L.text("6-stelligen Code eingeben"),text:$numericCode).textFieldStyle(.roundedBorder).font(.system(size:20,design:.monospaced)).frame(width:240).onSubmit{if numericCode.count==6{model.joinCode(numericCode)}}.onChange(of:numericCode){text in numericCode=String(text.filter{$0.isASCII && $0.isNumber}.prefix(6))}
                        Button("Verbinden"){model.joinCode(numericCode)}.buttonStyle(.borderedProminent).disabled(numericCode.count != 6)
                    }
                    DisclosureGroup(L.text("Weitere Möglichkeiten")) {
                    Button(L.text("QR-Code scannen")){cameraError="";model.showQrCamera=true}
                    Button(L.text("QR-Bild öffnen …")){model.scanQrImage()}
                    DisclosureGroup(L.text("Kopplungslink einfügen")){
                        SecureField(L.text("Link von Windows"),text:$model.pairingText)
                        Button("Verbinden"){model.joinQr(model.pairingText);model.pairingText=""}
                    }
                    }
                    Text(L.text("Beide Geräte müssen im selben Netzwerk sein. Kopra sucht den PC automatisch.")).font(.caption).foregroundStyle(.secondary)
                }
                if model.pairingInProgress && !model.pairingCanConfirm && !model.pairingApproved { ProgressView().controlSize(.small) }
                if !model.pairingViaCode && !model.pairingDigits.isEmpty{Text(model.pairingDigits).font(.system(size:34,weight:.medium,design:.monospaced)).frame(maxWidth:.infinity).padding(.vertical,10)}
                if !model.pairingStatus.isEmpty{Text(L.text(model.pairingStatus)).font(.caption).foregroundStyle(.secondary)}
                if model.pairingCanConfirm{Button(model.pairingViaCode ? L.text("Mit Windows verbinden"):L.text("Codes stimmen überein")){model.approveQr()}.buttonStyle(.borderedProminent)}
                if model.pairingInProgress && !model.pairingApproved{Button(L.text("Abbrechen")){model.cancelQr();model.pairingStatus="Kopplung abgebrochen."}}
            }else{
                Button(L.text("Verbindung aufheben"),role:.destructive){confirmDisconnect=true}.confirmationDialog(L.text("Verbindung mit Windows aufheben?"),isPresented:$confirmDisconnect,titleVisibility:.visible){Button(L.text("Verbindung aufheben"),role:.destructive){model.history.preferences.sharingEnabled=false;model.save();model.cancelQr();model.paired=false;do{try PairingVault.save(nil);model.status=L.text("Nur auf diesem Mac")}catch{model.pairingStatus=L.text("Die Verbindung ist aus. Der gespeicherte Schlüssel konnte nicht gelöscht werden.")}};Button(L.text("Abbrechen"),role:.cancel){}}message:{Text(L.text("Neue Kopien werden nicht mehr geteilt. Dein gespeicherter Verlauf bleibt auf beiden Geräten erhalten."))}
            }
            if model.paired {
            Toggle(L.text("Neue Kopien direkt zum Einfügen bereitstellen"),isOn:Binding(get:{model.history.preferences.sharedPaste},set:{model.history.preferences.sharedPaste=$0;model.save()}))
            Text(L.text("Offline nachgeholte Inhalte erscheinen im Verlauf. Deine neuere Zwischenablage bleibt erhalten.")).font(.caption).foregroundStyle(.secondary)
            }
        }
        .sheet(isPresented:$model.showQrCamera){
            VStack(alignment:.leading,spacing:14){Text(L.text("QR-Code auf Windows scannen")).font(.title2);CameraScanner(found:{model.showQrCamera=false;model.joinQr($0)},failed:{cameraError=$0}).frame(width:440,height:300).background(.black,in:RoundedRectangle(cornerRadius:12));if !cameraError.isEmpty{Text(cameraError).font(.caption)};HStack{Button(L.text("QR-Bild öffnen …")){model.showQrCamera=false;model.scanQrImage()};Spacer();Button(L.text("Schließen")){model.showQrCamera=false}}}.padding(22)
        }
        Section(L.text("Einfügen")){
            Button(L.text("Einfügen mit Enter erlauben")){let options=[kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary;_ = AXIsProcessTrustedWithOptions(options)}
            Text(L.text("macOS benötigt dafür die Bedienungshilfen-Freigabe. Normales Einfügen mit ⌘V funktioniert auch ohne sie.")).font(.caption).foregroundStyle(.secondary)
        }
    }
}
