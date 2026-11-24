import SwiftUI
import AppKit
import ApplicationServices

struct DevicePairingView:View {
    @ObservedObject var model:AppModel
    @State var cameraError=""
    var body:some View {
        Section("Gemeinsame Zwischenablage"){
            Label(model.paired ? "Mit Windows gekoppelt":"Noch kein Gerät gekoppelt",systemImage:model.paired ? "checkmark.shield":"laptopcomputer")
            Text("Du kannst Kopra auf diesem Mac allein nutzen. Verbinde einen Windows-Rechner, wenn du neue Kopien teilen möchtest.").font(.caption).foregroundStyle(.secondary)
            if !model.paired {
                if !model.pairingInProgress{
                    Button("QR-Code scannen"){cameraError="";model.showQrCamera=true}
                    Button("QR-Bild öffnen …"){model.scanQrImage()}
                    DisclosureGroup("Kopplungslink einfügen"){
                        SecureField("Link von Windows",text:$model.pairingText)
                        Button("Verbinden"){model.joinQr(model.pairingText);model.pairingText=""}
                    }
                    Text("Zeige den QR-Code unter Windows in Kopra → Einstellungen → Geräte an. Beide Geräte müssen im selben Netzwerk sein.").font(.caption).foregroundStyle(.secondary)
                }
                if !model.pairingDigits.isEmpty{Text(model.pairingDigits).font(.system(size:34,weight:.medium,design:.monospaced)).frame(maxWidth:.infinity).padding(.vertical,10)}
                if !model.pairingStatus.isEmpty{Text(model.pairingStatus).font(.caption).foregroundStyle(.secondary)}
                if model.pairingCanConfirm{Button("Codes stimmen überein"){model.approveQr()}.buttonStyle(.borderedProminent)}
                if model.pairingInProgress && !model.pairingApproved{Button("Abbrechen"){model.cancelQr();model.pairingStatus="Kopplung abgebrochen."}}
            }else{
                Button("Verbindung aufheben",role:.destructive){model.history.preferences.sharingEnabled=false;model.save();model.cancelQr();model.paired=false;do{try PairingVault.save(nil);model.status="Nur auf diesem Mac"}catch{model.pairingStatus="Die Verbindung ist aus. Der gespeicherte Schlüssel konnte nicht gelöscht werden."}}
            }
            Toggle("Neue Kopien direkt zum Einfügen bereitstellen",isOn:Binding(get:{model.history.preferences.sharedPaste},set:{model.history.preferences.sharedPaste=$0;model.save()}))
            Text("Offline nachgeholte Inhalte erscheinen im Verlauf. Deine neuere Zwischenablage bleibt erhalten.").font(.caption).foregroundStyle(.secondary)
        }
        .sheet(isPresented:$model.showQrCamera){
            VStack(alignment:.leading,spacing:14){Text("QR-Code auf Windows scannen").font(.title2);CameraScanner(found:{model.showQrCamera=false;model.joinQr($0)},failed:{cameraError=$0}).frame(width:440,height:300).background(.black,in:RoundedRectangle(cornerRadius:12));if !cameraError.isEmpty{Text(cameraError).font(.caption)};HStack{Button("QR-Bild öffnen …"){model.showQrCamera=false;model.scanQrImage()};Spacer();Button("Schließen"){model.showQrCamera=false}}}.padding(22)
        }
        Section("Einfügen"){
            Button("Einfügen mit Enter erlauben"){let options=[kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String:true] as CFDictionary;_ = AXIsProcessTrustedWithOptions(options)}
            Text("macOS benötigt dafür die Bedienungshilfen-Freigabe. Normales Einfügen mit ⌘V funktioniert auch ohne sie.").font(.caption).foregroundStyle(.secondary)
        }
    }
}
